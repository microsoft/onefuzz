#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from typing import List, Optional, Sequence, Tuple
from uuid import UUID

from memoization import cached
from onefuzztypes import models
from onefuzztypes.enums import ErrorCode, TaskState
from onefuzztypes.events import (
    EventCrashReported,
    EventFileAdded,
    EventRegressionReported,
)
from onefuzztypes.models import (
    ADOTemplate,
    Error,
    GithubIssueTemplate,
    NotificationTemplate,
    RegressionReport,
    Report,
    Result,
    TeamsTemplate,
)
from onefuzztypes.primitives import Container

from ..azure.containers import container_exists, get_file_sas_url
from ..azure.queue import send_message
from ..azure.storage import StorageType
from ..events import send_event
from ..orm import ORMMixin
from ..reports import get_report_or_regression
from ..tasks.config import get_input_container_queues
from ..tasks.main import Task
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


def get_regression_report_task(report: RegressionReport) -> Optional[Task]:
    # crash_test_result is required, but report & no_repro are not
    if report.crash_test_result.crash_report:
        return Task.get(
            report.crash_test_result.crash_report.job_id,
            report.crash_test_result.crash_report.task_id,
        )
    if report.crash_test_result.no_repro:
        return Task.get(
            report.crash_test_result.no_repro.job_id,
            report.crash_test_result.no_repro.task_id,
        )

    logging.error(
        "unable to find crash_report or no_repro entry for report: %s",
        report.json(include_none=False),
    )
    return None


@cached(ttl=10)
def get_queue_tasks() -> Sequence[Tuple[Task, Sequence[str]]]:
    results = []
    for task in Task.search_states(states=TaskState.available()):
        containers = get_input_container_queues(task.config)
        if containers:
            results.append((task, containers))
    return results


def new_files(
    container: Container, filename: str, fail_task_on_transient_error: bool
) -> None:
    notifications = get_notifications(container)

    report = get_report_or_regression(
        container, filename, expect_reports=bool(notifications)
    )

    if notifications:
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
                notify_ado(
                    notification.config,
                    container,
                    filename,
                    report,
                    fail_task_on_transient_error,
                )

            if isinstance(notification.config, GithubIssueTemplate):
                github_issue(notification.config, container, filename, report, True)

    for (task, containers) in get_queue_tasks():
        if container in containers:
            logging.info("queuing input %s %s %s", container, filename, task.task_id)
            url = get_file_sas_url(
                container, filename, StorageType.corpus, read=True, delete=True
            )
            send_message(task.task_id, bytes(url, "utf-8"), StorageType.corpus)

    if isinstance(report, Report):
        crash_report_event = EventCrashReported(
            report=report, container=container, filename=filename
        )
        report_task = Task.get(report.job_id, report.task_id)
        if report_task:
            crash_report_event.task_config = report_task.config
        send_event(crash_report_event)
    elif isinstance(report, RegressionReport):
        regression_event = EventRegressionReported(
            regression_report=report, container=container, filename=filename
        )

        report_task = get_regression_report_task(report)
        if report_task:
            regression_event.task_config = report_task.config
        send_event(regression_event)
    else:
        send_event(EventFileAdded(container=container, filename=filename))
