#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from typing import Optional

from onefuzztypes.enums import ContainerType

from .api import Command, Onefuzz
from .templates.afl import AFL
from .templates.libfuzzer import Libfuzzer
from .templates.ossfuzz import OssFuzz
from .templates.radamsa import Radamsa
from .templates.regression import Regression


class Template(Command):
    """Pre-defined job templates"""

    def __init__(self, onefuzz: Onefuzz, logger: logging.Logger) -> None:
        super().__init__(onefuzz, logger)
        self.libfuzzer = Libfuzzer(onefuzz, logger)
        self.afl = AFL(onefuzz, logger)
        self.radamsa = Radamsa(onefuzz, logger)
        self.ossfuzz = OssFuzz(onefuzz, logger)
        self.regression = Regression(onefuzz, logger)

    def stop(
        self,
        project: str,
        name: str,
        build: Optional[str],
        delete_containers: bool = False,
        stop_notifications: bool = False,
    ) -> None:
        SAFE_TO_REMOVE = [
            ContainerType.crashes,
            ContainerType.setup,
            ContainerType.inputs,
            ContainerType.reports,
            ContainerType.unique_inputs,
            ContainerType.unique_reports,
            ContainerType.no_repro,
            ContainerType.analysis,
            ContainerType.coverage,
            ContainerType.readonly_inputs,
        ]

        msg = ["project:%s" % project, "name:%s" % name]
        if build is not None:
            msg.append("build:%s" % build)
        self.logger.info("stopping %s" % " ".join(msg))

        jobs = self.onefuzz.jobs.list()
        for job in jobs:
            if job.config.project != project:
                continue

            if job.config.name != name:
                continue

            if build is not None and job.config.build != build:
                continue

            if job.state not in ["stopped", "stopping"]:
                self.logger.info("stopping job: %s", job.job_id)
                self.onefuzz.jobs.delete(job.job_id)

            tasks = self.onefuzz.tasks.list(job_id=job.job_id)
            for task in tasks:
                if task.state not in ["stopped"]:
                    self.logger.info("stopping task: %s", task.task_id)
                    self.onefuzz.tasks.delete(task.task_id)

                if delete_containers:
                    to_remove = []
                    for container in task.config.containers:
                        if container.type not in SAFE_TO_REMOVE:
                            self.logger.info("not removing: %s", container)
                            continue
                        to_remove.append(container.name)
                    for container_name in to_remove:
                        if self.onefuzz.containers.delete(container_name).result:
                            self.logger.info("removed container: %s", container_name)

                if stop_notifications:
                    notifications = self.onefuzz.notifications.list()
                    for container in task.config.containers:
                        for notification in notifications:
                            if container.name == notification.container:
                                self.logger.info(
                                    "removing notification: %s",
                                    notification.notification_id,
                                )
                                self.onefuzz.notifications.delete(
                                    notification.notification_id
                                )


Template.stop.__doc__ = "stop a template job"
