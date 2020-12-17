#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from typing import Dict, Generator, List, Optional, Tuple
from uuid import UUID, uuid4

from onefuzztypes.enums import OS, PoolState, TaskState
from onefuzztypes.models import TaskPool, TaskVm, WorkSet, WorkUnit
from pydantic import BaseModel

from ..azure.containers import StorageType, blob_exists, get_container_sas_url
from ..pools import Pool
from .config import build_task_config, get_setup_container
from .main import Task

HOURS = 60 * 60
MAX_TASKS_PER_VM = 10


def chunks(items: List, size: int) -> Generator:
    return (items[x : x + size] for x in range(0, len(items), size))


def schedule_workset(workset: WorkSet, pool: Pool, count: int) -> bool:
    if pool.state not in PoolState.available():
        logging.info(
            "pool not available for work: %s state: %s", pool.name, pool.state.name
        )
        return False

    for _ in range(count):
        if not pool.schedule_workset(workset):
            logging.error(
                "unable to schedule workset. pool:%s workset:%s", pool.name, workset
            )
            return False
    return True


class TaskBucketKey(BaseModel):
    os: OS
    job_id: UUID
    pool: Optional[TaskPool]
    vm: Optional[TaskVm]
    setup_container: str


def bucket_tasks(tasks: List[Task]) -> Dict[Tuple, List[Task]]:
    # buckets are hashed by:
    # OS, JOB ID, vm sku & image (if available), pool name (if available),
    #   if the setup script requires rebooting, and a 'unique' value
    #
    # The unique value is set based on the following conditions:
    # * if the task is set to run on more than one VM, than we assume it can't be shared
    # * if the task is missing the 'colocate' flag or it's set to False

    buckets: Dict[Tuple, List[Task]] = {}

    for task in tasks:
        vm: Optional[Tuple[str, str]] = None
        pool: Optional[str] = None
        unique: Optional[UUID] = None

        if task.config.vm:
            vm = (task.config.vm.sku, task.config.vm.image)
            if task.config.vm.count > 1:
                unique = uuid4()

        if task.config.pool:
            pool = task.config.pool.pool_name
            if task.config.pool.count > 1:
                unique = uuid4()

        if not task.config.colocate:
            unique = uuid4()

        key = (
            task.os,
            task.job_id,
            vm,
            pool,
            get_setup_container(task.config),
            task.config.task.reboot_after_setup,
            unique,
        )
        if key not in buckets:
            buckets[key] = []
        buckets[key].append(task)

    return buckets


class BucketConfig(BaseModel):
    count: int
    reboot: bool
    setup_url: str
    setup_script: Optional[str]
    pool: Pool


def schedule_tasks() -> None:
    tasks: List[Task] = []

    not_ready_count = 0

    for task in Task.search_states(states=[TaskState.waiting]):
        if not task.ready_to_schedule():
            not_ready_count += 1
            continue

        tasks.append(task)

    if not tasks and not_ready_count > 0:
        logging.info("tasks not ready: %d", not_ready_count)

    buckets = bucket_tasks(tasks)

    for bucketed_tasks in buckets.values():
        work_units = []

        bucket_config: Optional[BucketConfig] = None
        tasks_by_id = {}
        for task in bucketed_tasks:
            tasks_by_id[task.task_id] = task
            logging.info("scheduling task: %s", task.task_id)

            pool = task.get_pool()
            if not pool:
                logging.info("unable to find pool for task: %s", task.task_id)
                continue

            task_config = build_task_config(task.job_id, task.task_id, task.config)

            setup_container = get_setup_container(task.config)
            setup_url = get_container_sas_url(
                setup_container, StorageType.corpus, read=True, list=True
            )

            setup_script = None

            if task.os == OS.windows and blob_exists(
                setup_container, "setup.ps1", StorageType.corpus
            ):
                setup_script = "setup.ps1"
            if task.os == OS.linux and blob_exists(
                setup_container, "setup.sh", StorageType.corpus
            ):
                setup_script = "setup.sh"

            # save_blob(
            #    "task-configs",
            #    "%s/config.json" % task.task_id,
            #    agent_config.json(exclude_none=True),
            #    StorageType.config,
            # )
            reboot = False
            count = 1
            if task.config.pool:
                count = task.config.pool.count
                reboot = task.config.task.reboot_after_setup is True
            elif task.config.vm:
                # this branch should go away when we stop letting people specify
                # VM configs directly.
                count = task.config.vm.count
                reboot = (
                    task.config.vm.reboot_after_setup is True
                    or task.config.task.reboot_after_setup is True
                )
            else:
                raise TypeError

            work_unit = WorkUnit(
                job_id=task_config.job_id,
                task_id=task_config.task_id,
                task_type=task_config.task_type,
                config=task_config.json(),
            )
            work_units.append(work_unit)

            if bucket_config:
                assert bucket_config.reboot == reboot
                assert bucket_config.setup_script == setup_script
                assert bucket_config.setup_url == setup_url
            else:
                bucket_config = BucketConfig(
                    pool=pool,
                    count=count,
                    reboot=reboot,
                    setup_script=setup_script,
                    setup_url=setup_url,
                )

        assert len(work_units)
        assert bucket_config is not None

        for work_unit_chunks in chunks(work_units, MAX_TASKS_PER_VM):
            workset = WorkSet(
                reboot=bucket_config.reboot,
                script=(bucket_config.setup_script is not None),
                setup_url=bucket_config.setup_url,
                work_units=work_unit_chunks,
            )

            if schedule_workset(workset, bucket_config.pool, bucket_config.count):
                for work_set in workset.work_units:
                    task = tasks_by_id[work_set.task_id]
                    task.state = TaskState.scheduled
                    task.save()
