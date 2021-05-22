#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from collections import defaultdict
from typing import DefaultDict, List, Optional, Set
from uuid import UUID

from onefuzztypes.enums import ContainerType, JobState
from onefuzztypes.primitives import Container

from ..api import UUID_EXPANSION, Command
from .cache import JobFilter
from .raw import raw
from .top import Top


def short(value: UUID) -> str:
    return str(value).split("-")[0]


class Status(Command):
    """Monitor status of Onefuzz Instance"""

    def project(
        self,
        *,
        project: str,
        name: str,
        build: Optional[str] = None,
        all_jobs: Optional[bool] = False,
    ) -> None:
        job_state = JobState.available()
        if all_jobs:
            job_state = [x for x in JobState]

        for job in self.onefuzz.jobs.list(job_state=job_state):
            if job.config.project != project:
                continue
            if job.config.name != name:
                continue
            if build is not None and job.config.build != build:
                continue

            self.job(job.job_id)

    def job(self, job_id: UUID_EXPANSION) -> None:
        job = self.onefuzz.jobs.get(job_id)

        print(f"job: {job.job_id}")
        print(
            f"project:{job.config.project} name:{job.config.name} build:{job.config.build}"
        )
        print("\ntasks:")

        tasks = self.onefuzz.tasks.list(job_id=job.job_id, state=[])
        with_errors = []
        for task in tasks:
            if task.error:
                with_errors.append(task)
                continue
            print(
                f"{short(task.task_id)} "
                f"target:{task.config.task.target_exe} "
                f"state:{task.state.name} "
                f"type:{task.config.task.type.name}"
            )

        if with_errors:
            print("\ntasks with errors:")
            entries = []
            for task in with_errors:
                if not task.error:
                    continue

                errors = "\n".join([x.strip() for x in task.error.errors])

                message = (
                    f"{short(task.task_id)} type:{task.config.task.type.name} "
                    f"target:{task.config.task.target_exe}"
                    f"\nerror:\n{errors}"
                )
                entries.append(message)
            print("\n\n".join(entries))

        containers: DefaultDict[ContainerType, Set[Container]] = defaultdict(set)
        for task in tasks:
            for container in task.config.containers:
                if container.type not in containers:
                    containers[container.type] = set()
                containers[container.type].add(container.name)

        print("\ncontainers:")
        for container_type in containers:
            for container_name in containers[container_type]:
                try:
                    count = len(
                        self.onefuzz.containers.files.list(container_name).files
                    )
                    print(
                        f"{container_type.name:<15} count:{count:<5} name:{container_name}"
                    )
                except Exception:
                    print(
                        f"{container_type.name:<15} INACCESSIBLE CONTAINER name:{container_name}"
                    )

    def raw(self) -> None:
        """Raw status update stream"""
        raw(self.onefuzz, self.logger)

    def top(
        self,
        *,
        show_details: bool = False,
        job_id: Optional[List[UUID]] = None,
        project: Optional[List[str]] = None,
        name: Optional[List[str]] = None,
    ) -> None:
        """Onefuzz Top"""
        job_filter = JobFilter(job_id=job_id, project=project, name=name)
        top = Top(self.onefuzz, self.logger, show_details, job_filter)
        top.run()
