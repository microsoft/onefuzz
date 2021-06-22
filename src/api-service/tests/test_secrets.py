#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
import pathlib
import unittest

from onefuzztypes.enums import OS, ContainerType
from onefuzztypes.job_templates import (
    JobTemplate,
    JobTemplateIndex,
    JobTemplateNotification,
)
from onefuzztypes.models import (
    ADOTemplate,
    GithubAuth,
    GithubIssueTemplate,
    JobConfig,
    Notification,
    NotificationConfig,
    SecretAddress,
    SecretData,
    TeamsTemplate,
)
from onefuzztypes.primitives import Container
from onefuzztypes.requests import NotificationCreate

from __app__.onefuzzlib.orm import hide_secrets


def hider(secret_data: SecretData) -> SecretData:
    if not isinstance(secret_data.secret, SecretAddress):
        secret_data.secret = SecretAddress(url="blah blah")
    return secret_data


class TestSecret(unittest.TestCase):
    def test_hide(self) -> None:
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

    def test_roundtrip_github_issue(self) -> None:
        current_path = pathlib.Path(__file__).parent.absolute()
        with open(
            f"{current_path}"
            + "/../../../contrib/onefuzz-job-github-actions/github-issues.json"
        ) as json_file:
            notification_dict = json.load(json_file)
            notification_dict["container"] = "testing"
            notification1 = NotificationCreate.parse_obj(notification_dict)

            assert isinstance(notification1.config, GithubIssueTemplate)
            self.assertIsInstance(
                notification1.config.auth.secret, GithubAuth, "Invalid secret type"
            )

            notification2 = NotificationCreate.parse_obj(
                json.loads(notification1.json())
            )

            assert isinstance(notification2.config, GithubIssueTemplate)
            self.assertIsInstance(
                notification2.config.auth.secret, GithubAuth, "Invalid secret type"
            )

            hide_secrets(notification2, hider)

            notification3 = NotificationCreate.parse_obj(
                json.loads(notification2.json())
            )
            assert isinstance(notification2.config, GithubIssueTemplate)
            self.assertIsInstance(
                notification3.config.auth.secret, SecretAddress, "Invalid secret type"
            )

    def test_roundtrip_team_issue(self) -> None:
        a = """
            {
                "config" : {"url": "http://test"},
                "container": "testing"
                }

        """  # noqa
        notification_dict = json.loads(a)
        notification_dict["container"] = "testing"
        notification1 = NotificationCreate.parse_obj(notification_dict)

        assert isinstance(notification1.config, TeamsTemplate)
        self.assertIsInstance(
            notification1.config.url.secret, str, "Invalid secret type"
        )

        notification2 = NotificationCreate.parse_obj(json.loads(notification1.json()))
        assert isinstance(notification2.config, TeamsTemplate)
        self.assertIsInstance(
            notification2.config.url.secret, str, "Invalid secret type"
        )

        hide_secrets(notification2, hider)

        notification3 = NotificationCreate.parse_obj(json.loads(notification2.json()))
        assert isinstance(notification3.config, TeamsTemplate)
        self.assertIsInstance(
            notification3.config.url.secret, SecretAddress, "Invalid secret type"
        )

    def test_roundtrip_ado(self) -> None:
        current_path = pathlib.Path(__file__).parent.absolute()
        with open(
            f"{current_path}"
            + "/../../../contrib/onefuzz-job-azure-devops-pipeline/ado-work-items.json"  # noqa
        ) as json_file:
            notification_dict = json.load(json_file)
            notification_dict["container"] = "testing"
            notification1 = NotificationCreate.parse_obj(notification_dict)
            assert isinstance(notification1.config, ADOTemplate)
            self.assertIsInstance(
                notification1.config.auth_token.secret, str, "Invalid secret type"
            )

            notification2 = NotificationCreate.parse_obj(
                json.loads(notification1.json())
            )
            assert isinstance(notification2.config, ADOTemplate)
            self.assertIsInstance(
                notification2.config.auth_token.secret, str, "Invalid secret type"
            )

            hide_secrets(notification2, hider)

            notification3 = NotificationCreate.parse_obj(
                json.loads(notification2.json())
            )
            assert isinstance(notification3.config, ADOTemplate)
            self.assertIsInstance(
                notification3.config.auth_token.secret,
                SecretAddress,
                "Invalid secret type",
            )
