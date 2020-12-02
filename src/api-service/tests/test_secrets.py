#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import unittest

from onefuzztypes.models import (
    Container,
    Notification,
    SecretAddress,
    SecretData,
    TeamsTemplate,
)

from __app__.onefuzzlib.orm import ORMMixin


class TestQueryBuilder(unittest.TestCase):
    def test_hide(self) -> None:
        def hider(secret_data: SecretData) -> None:
            if not isinstance(secret_data.secret, SecretAddress):
                secret_data.secret = SecretAddress(url="blah blah")

        notification = Notification(
            container=Container("data"), config=TeamsTemplate(url="http://test")
        )
        ORMMixin.hide_secrets(notification, hider)
        self.assertIsInstance(notification.config.url, SecretData)
        self.assertIsInstance(notification.config.url.secret, SecretAddress)
