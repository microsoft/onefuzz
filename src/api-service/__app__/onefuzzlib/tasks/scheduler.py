#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from typing import Dict, List
from uuid import UUID

from onefuzztypes.enums import OS, TaskState
from onefuzztypes.models import WorkSet, WorkUnit

from ..azure.containers import blob_exists, get_container_sas_url, save_blob
from ..azure.creds import get_func_storage
from .config import build_task_config, get_setup_container
from .main import Task

HOURS = 60 * 60


def schedule_tasks() -> None:
    to_schedule: Dict[UUID, List[Task]] = {}

    for task in Task.search_states(states=[TaskState.waiting]):
        if not task.ready_to_schedule():
            continue

        if task.job_id not in to_schedule:
            to_schedule[task.job_id] = []
        to_schedule[task.job_id].append(task)

    for tasks in to_schedule.values():
        # TODO: for now, we're only scheduling one task per VM.

        for task in tasks:
            logging.info("scheduling task: %s", task.task_id)
            agent_config = build_task_config(task.job_id, task.task_id, task.config)

            setup_container = get_setup_container(task.config)
            setup_url = get_container_sas_url(setup_container, read=True, list=True)

            setup_script = None

            if task.os == OS.windows and blob_exists(setup_container, "setup.ps1"):
                setup_script = "setup.ps1"
            if task.os == OS.linux and blob_exists(setup_container, "setup.sh"):
                setup_script = "setup.sh"

            save_blob(
                "task-configs",
                "%s/config.json" % task.task_id,
                agent_config.json(),
                account_id=get_func_storage(),
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
            work_set = WorkSet(
                reboot=reboot,
                script=(setup_script is not None),
                setup_url=setup_url,
                work_units=[work_unit],
            )

            pool = task.get_pool()
            if not pool:
                logging.info("unable to find pool for task: %s", task.task_id)
                continue

            for _ in range(count):
                pool.schedule_workset(work_set)
            task.state = TaskState.scheduled
            task.save()
