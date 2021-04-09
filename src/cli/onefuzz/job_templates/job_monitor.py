#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from typing import Any, Dict, List, Optional, Tuple

from onefuzztypes.enums import JobState, TaskState
from onefuzztypes.models import Job, Task
from onefuzztypes.primitives import Container

from ..api import Onefuzz
from ..backend import wait


class StoppedEarly(Exception):
    pass


class JobMonitor:
    def __init__(self, onefuzz: Onefuzz, job: Job):
        self.onefuzz = onefuzz
        self.job = job
        self.containers: Dict[Container, int] = {}

    def get_running_tasks_checked(self) -> List[Task]:
        self.job = self.onefuzz.jobs.get(self.job.job_id)
        if self.job.state in JobState.shutting_down():
            raise StoppedEarly("job unexpectedly stopped early")

        errors = []
        tasks = []
        for task in self.onefuzz.tasks.list(job_id=self.job.job_id):
            if task.state in TaskState.shutting_down():
                if task.error:
                    errors.append("%s: %s" % (task.config.task.type, task.error))
                else:
                    errors.append("%s" % task.config.task.type)
            tasks.append(task)

        if errors:
            raise StoppedEarly("tasks stopped unexpectedly.\n%s" % "\n".join(errors))
        return tasks

    def get_waiting(self) -> List[str]:
        tasks = self.get_running_tasks_checked()

        waiting = []
        for task in tasks:
            state_msg = task.state.name
            if task.state in TaskState.has_started():
                task = self.onefuzz.tasks.get(task.task_id)
                if task.events:
                    continue
                state_msg = "waiting-for-heartbeat"

            waiting.append(f"{task.config.task.type.name}:{state_msg}")
        return waiting

    def is_running(self) -> Tuple[bool, str, Any]:
        waiting = self.get_waiting()
        return (not waiting, "waiting on: %s" % ", ".join(sorted(waiting)), None)

    def has_files(self) -> Tuple[bool, str, Any]:
        self.get_running_tasks_checked()

        new = {
            x: len(self.onefuzz.containers.files.list(x).files)
            for x in self.containers.keys()
        }

        for container in new:
            if new[container] > self.containers[container]:
                del self.containers[container]
        return (
            not self.containers,
            "waiting for new files: %s" % ", ".join(self.containers.keys()),
            None,
        )

    def is_stopped(self) -> Tuple[bool, str, Any]:
        tasks = self.onefuzz.tasks.list(job_id=self.job.job_id)
        waiting = [
            "%s:%s" % (x.config.task.type.name, x.state.name)
            for x in tasks
            if x.state != TaskState.stopped
        ]
        return (not waiting, "waiting on: %s" % ", ".join(sorted(waiting)), None)

    def wait(
        self,
        *,
        wait_for_running: Optional[bool] = False,
        wait_for_files: Optional[Dict[Container, int]] = None,
        wait_for_stopped: Optional[bool] = False,
    ) -> None:
        if wait_for_running:
            wait(self.is_running)
            self.onefuzz.logger.info("tasks started")

        if wait_for_files:
            self.containers = wait_for_files
            wait(self.has_files)
            self.onefuzz.logger.info("new files found")

        if wait_for_stopped:
            wait(self.is_stopped)
            self.onefuzz.logger.info("tasks stopped")
