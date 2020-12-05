#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import unittest

from onefuzztypes.enums import OS
from onefuzztypes.job_templates import (
    JobConfig,
    JobTemplate,
    JobTemplateIndex,
    JobTemplateNotification,
)
from onefuzztypes.models import (
    ContainerType,
    Notification,
    NotificationConfig,
    SecretAddress,
    SecretData,
    TeamsTemplate,
)
from onefuzztypes.primitives import Container

from __app__.onefuzzlib.orm import ORMMixin


class TestQueryBuilder(unittest.TestCase):
    def test_hide(self) -> None:
        def hider(secret_data: SecretData) -> None:
            if not isinstance(secret_data.secret, SecretAddress):
                secret_data.secret = SecretAddress(url="blah blah")

        notification = Notification(
            container=Container("data"),
            config=TeamsTemplate(url=SecretData(secret="http://test")),
        )
        ORMMixin.hide_secrets(notification, hider)

        if isinstance(notification.config, TeamsTemplate):
            self.assertIsInstance(notification.config.url, SecretData)
            self.assertIsInstance(notification.config.url.secret, SecretAddress)
        else:
            self.fail(f"Invalid config type {type(notification.config)}")

    def test_hide_nested_list(self) -> None:
        def hider(secret_data: SecretData) -> None:
            if not isinstance(secret_data.secret, SecretAddress):
                secret_data.secret = SecretAddress(url="blah blah")

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
        ORMMixin.hide_secrets(job_template_index, hider)
        notification = job_template_index.template.notifications[0].notification
        if isinstance(notification.config, TeamsTemplate):
            self.assertIsInstance(notification.config.url, SecretData)
            self.assertIsInstance(notification.config.url.secret, SecretAddress)
        else:
            self.fail(f"Invalid config type {type(notification.config)}")
