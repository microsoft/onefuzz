#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from typing import Dict, List
from uuid import UUID

from onefuzztypes.enums import OS, PoolState, TaskState
from onefuzztypes.models import WorkSet, WorkUnit

from ..azure.containers import (
    StorageType,
    blob_exists,
    get_container_sas_url,
    save_blob,
)
from ..pools import Pool
from .config import build_task_config, get_setup_container
from .main import Task

HOURS = 60 * 60


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


def schedule_tasks() -> None:
    to_schedule: Dict[UUID, List[Task]] = {}

    not_ready_count = 0

    for task in Task.search_states(states=[TaskState.waiting]):
        if not task.ready_to_schedule():
            not_ready_count += 1
            continue

        if task.job_id not in to_schedule:
            to_schedule[task.job_id] = []
        to_schedule[task.job_id].append(task)

    if not to_schedule and not_ready_count > 0:
        logging.info("tasks not ready: %d", not_ready_count)

    for tasks in to_schedule.values():
        # TODO: for now, we're only scheduling one task per VM.

        for task in tasks:
            logging.info("scheduling task: %s", task.task_id)
            agent_config = build_task_config(task.job_id, task.task_id, task.config)

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

            save_blob(
                "task-configs",
                "%s/config.json" % task.task_id,
                agent_config.json(exclude_none=True),
                StorageType.config,
            )
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

            task_config = agent_config
            task_config_json = task_config.json()
            work_unit = WorkUnit(
                job_id=task_config.job_id,
                task_id=task_config.task_id,
                task_type=task_config.task_type,
                config=task_config_json,
            )

            # For now, only offer singleton work sets.
            workset = WorkSet(
                reboot=reboot,
                script=(setup_script is not None),
                setup_url=setup_url,
                work_units=[work_unit],
            )

            pool = task.get_pool()
            if not pool:
                logging.info("unable to find pool for task: %s", task.task_id)
                continue

            if schedule_workset(workset, pool, count):
                task.state = TaskState.scheduled
                task.save()
