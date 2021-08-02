#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from typing import Dict, Generator, List, Optional, Tuple, TypeVar
from uuid import UUID, uuid4

from onefuzztypes.enums import OS, PoolState, TaskState
from onefuzztypes.models import WorkSet, WorkUnit
from onefuzztypes.primitives import Container
from pydantic import BaseModel

from ..azure.containers import blob_exists, get_container_sas_url
from ..azure.storage import StorageType
from ..workers.pools import Pool
from .config import build_task_config, get_setup_container
from .main import Task

HOURS = 60 * 60

# TODO: eventually, this should be tied to the pool.
MAX_TASKS_PER_SET = 10


A = TypeVar("A")


def chunks(items: List[A], size: int) -> Generator[List[A], None, None]:
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


# TODO - Once Pydantic supports hashable models, the Tuple should be replaced
# with a model.
#
# For info: https://github.com/samuelcolvin/pydantic/pull/1881


def bucket_tasks(tasks: List[Task]) -> Dict[Tuple, List[Task]]:
    # buckets are hashed by:
    # OS, JOB ID, vm sku & image (if available), pool name (if available),
    # if the setup script requires rebooting, and a 'unique' value
    #
    # The unique value is set based on the following conditions:
    # * if the task is set to run on more than one VM, than we assume it can't be shared
    # * if the task is missing the 'colocate' flag or it's set to False

    buckets: Dict[Tuple, List[Task]] = {}

    for task in tasks:
        vm: Optional[Tuple[str, str]] = None
        pool: Optional[str] = None
        unique: Optional[UUID] = None

        # check for multiple VMs for pre-1.0.0 tasks
        if task.config.vm:
            vm = (task.config.vm.sku, task.config.vm.image)
            if task.config.vm.count > 1:
                unique = uuid4()

        # check for multiple VMs for 1.0.0 and later tasks
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
    setup_container: Container
    setup_script: Optional[str]
    pool: Pool


def build_work_unit(task: Task) -> Optional[Tuple[BucketConfig, WorkUnit]]:
    pool = task.get_pool()
    if not pool:
        logging.info("unable to find pool for task: %s", task.task_id)
        return None

    logging.info("scheduling task: %s", task.task_id)

    task_config = build_task_config(task.job_id, task.task_id, task.config)

    setup_container = get_setup_container(task.config)
    setup_script = None

    if task.os == OS.windows and blob_exists(
        setup_container, "setup.ps1", StorageType.corpus
    ):
        setup_script = "setup.ps1"
    if task.os == OS.linux and blob_exists(
        setup_container, "setup.sh", StorageType.corpus
    ):
        setup_script = "setup.sh"

    reboot = False
    count = 1
    if task.config.pool:
        count = task.config.pool.count

        # NOTE: "is True" is required to handle Optional[bool]
        reboot = task.config.task.reboot_after_setup is True
    elif task.config.vm:
        # this branch should go away when we stop letting people specify
        # VM configs directly.
        count = task.config.vm.count

        # NOTE: "is True" is required to handle Optional[bool]
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
        config=task_config.json(exclude_none=True, exclude_unset=True),
    )

    bucket_config = BucketConfig(
        pool=pool,
        count=count,
        reboot=reboot,
        setup_script=setup_script,
        setup_container=setup_container,
    )

    return bucket_config, work_unit


def build_work_set(tasks: List[Task]) -> Optional[Tuple[BucketConfig, WorkSet]]:
    task_ids = [x.task_id for x in tasks]

    bucket_config: Optional[BucketConfig] = None
    work_units = []

    for task in tasks:
        if task.config.prereq_tasks:
            # if all of the prereqs are in this bucket, they will be
            # scheduled together
            if not all([task_id in task_ids for task_id in task.config.prereq_tasks]):
                if not task.check_prereq_tasks():
                    continue

        result = build_work_unit(task)
        if not result:
            continue

        new_bucket_config, work_unit = result
        if bucket_config is None:
            bucket_config = new_bucket_config
        else:
            if bucket_config != new_bucket_config:
                raise Exception(
                    f"bucket configs differ: {bucket_config} VS {new_bucket_config}"
                )

        work_units.append(work_unit)

    if bucket_config:
        setup_url = get_container_sas_url(
            bucket_config.setup_container, StorageType.corpus, read=True, list_=True
        )

        work_set = WorkSet(
            reboot=bucket_config.reboot,
            script=(bucket_config.setup_script is not None),
            setup_url=setup_url,
            work_units=work_units,
        )
        return (bucket_config, work_set)

    return None


def schedule_tasks() -> None:
    tasks: List[Task] = []

    tasks = Task.search_states(states=[TaskState.waiting])

    tasks_by_id = {x.task_id: x for x in tasks}
    seen = set()

    not_ready_count = 0

    buckets = bucket_tasks(tasks)

    for bucketed_tasks in buckets.values():
        for chunk in chunks(bucketed_tasks, MAX_TASKS_PER_SET):
            result = build_work_set(chunk)
            if result is None:
                continue
            bucket_config, work_set = result

            if schedule_workset(work_set, bucket_config.pool, bucket_config.count):
                for work_unit in work_set.work_units:
                    task = tasks_by_id[work_unit.task_id]
                    task.set_state(TaskState.scheduled)
                    seen.add(task.task_id)

    not_ready_count = len(tasks) - len(seen)
    if not_ready_count > 0:
        logging.info("tasks not ready: %d", not_ready_count)
