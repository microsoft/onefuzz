#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from typing import Any, Dict, List, Optional, Union

import requests
from onefuzztypes.models import RegressionReport, Report, TeamsTemplate
from onefuzztypes.primitives import Container

from ..azure.containers import auth_download_url
from ..secrets import get_secret_string_value
from ..tasks.config import get_setup_container
from ..tasks.main import Task


def markdown_escape(data: str) -> str:
    values = r"\\*_{}[]()#+-.!"  # noqa: P103
    for value in values:
        data = data.replace(value, "\\" + value)
    data = data.replace("`", "``")
    return data


def code_block(data: str) -> str:
    data = data.replace("`", "``")
    return "\n```\n%s\n```\n" % data


def send_teams_webhook(
    config: TeamsTemplate,
    title: str,
    facts: List[Dict[str, str]],
    text: Optional[str],
) -> None:
    title = markdown_escape(title)

    message: Dict[str, Any] = {
        "@type": "MessageCard",
        "@context": "https://schema.org/extensions",
        "summary": title,
        "sections": [{"activityTitle": title, "facts": facts}],
    }

    if text:
        message["sections"].append({"text": text})

    config_url = get_secret_string_value(config.url)
    response = requests.post(config_url, json=message)
    if not response.ok:
        logging.error("webhook failed %s %s", response.status_code, response.content)


def notify_teams(
    config: TeamsTemplate,
    container: Container,
    filename: str,
    report: Optional[Union[Report, RegressionReport]],
) -> None:
    text = None
    facts: List[Dict[str, str]] = []

    if isinstance(report, Report):
        task = Task.get(report.job_id, report.task_id)
        if not task:
            logging.error(
                "report with invalid task %s:%s", report.job_id, report.task_id
            )
            return

        title = "new crash in %s: %s @ %s" % (
            report.executable,
            report.crash_type,
            report.crash_site,
        )

        links = [
            "[report](%s)" % auth_download_url(container, filename),
        ]

        setup_container = get_setup_container(task.config)
        if setup_container:
            links.append(
                "[executable](%s)"
                % auth_download_url(
                    setup_container,
                    report.executable.replace("setup/", "", 1),
                ),
            )

        if report.input_blob:
            links.append(
                "[input](%s)"
                % auth_download_url(
                    report.input_blob.container, report.input_blob.name
                ),
            )

        facts += [
            {"name": "Files", "value": " | ".join(links)},
            {
                "name": "Task",
                "value": markdown_escape(
                    "job_id: %s task_id: %s" % (report.job_id, report.task_id)
                ),
            },
            {
                "name": "Repro",
                "value": code_block(
                    "onefuzz repro create_and_connect %s %s" % (container, filename)
                ),
            },
        ]

        text = "## Call Stack\n" + "\n".join(code_block(x) for x in report.call_stack)

    else:
        title = "new file found"
        facts += [
            {
                "name": "file",
                "value": "[%s/%s](%s)"
                % (
                    markdown_escape(container),
                    markdown_escape(filename),
                    auth_download_url(container, filename),
                ),
            }
        ]

    send_teams_webhook(config, title, facts, text)
