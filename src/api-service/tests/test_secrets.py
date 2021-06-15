#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
import unittest

from __app__.onefuzzlib.orm import hide_secrets
from onefuzztypes.enums import OS, ContainerType
from onefuzztypes.job_templates import (
    JobTemplate,
    JobTemplateIndex,
    JobTemplateNotification,
)
from onefuzztypes.models import (
    GithubAuth,
    GithubIssueTemplate,
    JobConfig,
    Notification,
    NotificationConfig,
    NotificationTemplate,
    SecretAddress,
    SecretData,
    TeamsTemplate,
)
from onefuzztypes.primitives import Container
from onefuzztypes.requests import NotificationCreate


class TestSecret(unittest.TestCase):
    def test_hide(self) -> None:
        def hider(secret_data: SecretData) -> SecretData:
            if not isinstance(secret_data.secret, SecretAddress):
                secret_data.secret = SecretAddress(url="blah blah")
            return secret_data

        notification = Notification(
            container=Container("data"),
            config=TeamsTemplate(url=SecretData(secret="http://test")),
        )
        notification = hide_secrets(notification, hider)

        if isinstance(notification.config, TeamsTemplate):
            self.assertIsInstance(notification.config.url, SecretData)
            self.assertIsInstance(notification.config.url.secret, SecretAddress)
        else:
            self.fail(f"Invalid config type {type(notification.config)}")

    def test_hide_nested_list(self) -> None:
        def hider(secret_data: SecretData) -> SecretData:
            if not isinstance(secret_data.secret, SecretAddress):
                secret_data.secret = SecretAddress(url="blah blah")
            return secret_data

        job_template_index = JobTemplateIndex(
            name="test",
            template=JobTemplate(
                os=OS.linux,
                job=JobConfig(name="test", build="test", project="test", duration=1),
                tasks=[],
                notifications=[
                    JobTemplateNotification(
                        container_type=ContainerType.unique_inputs,
                        notification=NotificationConfig(
                            config=TeamsTemplate(url=SecretData(secret="http://test"))
                        ),
                    )
                ],
                user_fields=[],
            ),
        )
        job_template_index = hide_secrets(job_template_index, hider)
        notification = job_template_index.template.notifications[0].notification
        if isinstance(notification.config, TeamsTemplate):
            self.assertIsInstance(notification.config.url, SecretData)
            self.assertIsInstance(notification.config.url.secret, SecretAddress)
        else:
            self.fail(f"Invalid config type {type(notification.config)}")

    def test_read_secret(self) -> None:
        json_data = """
            {
                "notification_id": "b52b24d1-eec6-46c9-b06a-818a997da43c",
                "container": "data",
                "config" : {"url": {"secret": {"url": "http://test"}}}
            }
            """
        data = json.loads(json_data)
        notification = Notification.parse_obj(data)
        self.assertIsInstance(notification.config, TeamsTemplate)
        if isinstance(notification.config, TeamsTemplate):
            self.assertIsInstance(notification.config.url, SecretData)
            self.assertIsInstance(notification.config.url.secret, SecretAddress)
        else:
            self.fail(f"Invalid config type {type(notification.config)}")

    def test_read_secret2(self) -> None:
        json_data = """
            {
                "notification_id": "b52b24d1-eec6-46c9-b06a-818a997da43c",
                "container": "data",
                "config" : {"url": {"secret": "http://test" }}
            }
            """
        data = json.loads(json_data)
        notification = Notification.parse_obj(data)
        self.assertIsInstance(notification.config, TeamsTemplate)
        if isinstance(notification.config, TeamsTemplate):
            self.assertIsInstance(notification.config.url, SecretData)
            self.assertIsInstance(notification.config.url.secret, str)
        else:
            self.fail(f"Invalid config type {type(notification.config)}")

    def test_read_secret3(self) -> None:
        json_data = """
            {
                "notification_id": "b52b24d1-eec6-46c9-b06a-818a997da43c",
                "container": "data",
                 "config": {
                    "auth": {
                        "secret": {
                            "user": "INSERT_YOUR_USERNAME_HERE",
                            "personal_access_token": "INSERT_YOUR_PERSONAL_ACCESS_TOKEN_HERE"
                        }
                    },
                    "organization": "contoso",
                    "repository": "sample-project",
                    "title": "{{ report.executable }} - {{report.crash_site}}",
                    "body": "",
                    "unique_search": {
                        "author": null,
                        "state": null,
                        "field_match": ["title"],
                        "string": "{{ report.executable }}"
                    },
                    "assignees": [],
                    "labels": ["bug", "{{ report.crash_type }}"],
                    "on_duplicate": {
                        "comment": "",
                        "labels": ["{{ report.crash_type }}"],
                        "reopen": true
                    }
                }
            }
            """
        data = json.loads(json_data)
        notification = Notification.parse_obj(data)
        self.assertIsInstance(notification.config, GithubIssueTemplate)
        if isinstance(notification.config, GithubIssueTemplate):
            self.assertIsInstance(notification.config.auth, SecretData)
            self.assertIsInstance(notification.config.auth.secret, GithubAuth)
        else:
            self.fail(f"Invalid config type {type(notification.config)}")
