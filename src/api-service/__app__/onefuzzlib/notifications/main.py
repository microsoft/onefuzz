#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from typing import Dict, List, Optional, Sequence, Tuple
from uuid import UUID

from memoization import cached
from onefuzztypes import models
from onefuzztypes.enums import ErrorCode, TaskState
from onefuzztypes.models import (
    ADOTemplate,
    Error,
    GithubIssueTemplate,
    NotificationTemplate,
    Result,
    TeamsTemplate,
)
from onefuzztypes.primitives import Container, Event
from onefuzztypes.webhooks import WebhookEventCrashReportCreated

from ..azure.containers import (
    StorageType,
    container_exists,
    get_container_metadata,
    get_file_sas_url,
)
from ..azure.queue import send_message
from ..dashboard import add_event
from ..orm import ORMMixin
from ..reports import get_report
from ..tasks.config import get_input_container_queues
from ..tasks.main import Task
from ..webhooks import Webhook
from .ado import notify_ado
from .github_issues import github_issue
from .teams import notify_teams


class Notification(models.Notification, ORMMixin):
    @classmethod
    def get_by_id(cls, notification_id: UUID) -> Result["Notification"]:
        notifications = cls.search(query={"notification_id": [notification_id]})
        if not notifications:
            return Error(
                code=ErrorCode.INVALID_REQUEST, errors=["unable to find Notification"]
            )

        if len(notifications) != 1:
            return Error(
                code=ErrorCode.INVALID_REQUEST,
                errors=["error identifying Notification"],
            )
        notification = notifications[0]
        return notification

    @classmethod
    def get_existing(
        cls, container: Container, config: NotificationTemplate
    ) -> Optional["Notification"]:
        notifications = Notification.search(query={"container": [container]})
        for notification in notifications:
            if notification.config == config:
                return notification
        return None

    @classmethod
    def key_fields(cls) -> Tuple[str, str]:
        return ("notification_id", "container")

    @classmethod
    def create(
        cls, container: Container, config: NotificationTemplate
    ) -> Result["Notification"]:
        if not container_exists(container, StorageType.corpus):
            return Error(code=ErrorCode.INVALID_REQUEST, errors=["invalid container"])

        existing = cls.get_existing(container, config)
        if existing is not None:
            return existing

        entry = cls(container=container, config=config)
        entry.save()
        logging.info(
            "created notification.  notification_id:%s container:%s",
            entry.notification_id,
            entry.container,
        )
        return entry


@cached(ttl=10)
def get_notifications(container: Container) -> List[Notification]:
    return Notification.search(query={"container": [container]})


@cached(ttl=10)
def get_queue_tasks() -> Sequence[Tuple[Task, Sequence[str]]]:
    results = []
    for task in Task.search_states(states=TaskState.available()):
        containers = get_input_container_queues(task.config)
        if containers:
            results.append((task, containers))
    return results


@cached(ttl=60)
def container_metadata(container: Container) -> Optional[Dict[str, str]]:
    return get_container_metadata(container, StorageType.corpus)


def new_files(container: Container, filename: str) -> None:
    results: Dict[str, Event] = {"container": container, "file": filename}

    metadata = container_metadata(container)
    if metadata:
        results["metadata"] = metadata

    notifications = get_notifications(container)
    if notifications:
        report = get_report(container, filename)
        if report:
            results["executable"] = report.executable
            results["crash_type"] = report.crash_type
            results["crash_site"] = report.crash_site
            results["job_id"] = report.job_id
            results["task_id"] = report.task_id

            Webhook.send_event(
                WebhookEventCrashReportCreated(
                    container=container, filename=filename, report=report
                )
            )

        logging.info("notifications for %s %s %s", container, filename, notifications)
        done = []
        for notification in notifications:
            # ignore duplicate configurations
            if notification.config in done:
                continue
            done.append(notification.config)

            if isinstance(notification.config, TeamsTemplate):
                notify_teams(notification.config, container, filename, report)

            if not report:
                continue

            if isinstance(notification.config, ADOTemplate):
                notify_ado(notification.config, container, filename, report)

            if isinstance(notification.config, GithubIssueTemplate):
                github_issue(notification.config, container, filename, report)

    for (task, containers) in get_queue_tasks():
        if container in containers:
            logging.info("queuing input %s %s %s", container, filename, task.task_id)
            url = get_file_sas_url(
                container, filename, StorageType.corpus, read=True, delete=True
            )
            send_message(task.task_id, bytes(url, "utf-8"), StorageType.corpus)

    add_event("new_file", results)
