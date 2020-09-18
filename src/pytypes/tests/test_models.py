#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import unittest

from pydantic import ValidationError

from onefuzztypes.models import TeamsTemplate
from onefuzztypes.requests import NotificationCreate


class TestModelsVerify(unittest.TestCase):
    def test_model(self) -> None:
        data = {
            "container": "data",
            "config": {"url": "https://www.contoso.com/"},
        }

        notification = NotificationCreate.parse_obj(data)
        self.assertIsInstance(notification.config, TeamsTemplate)

        missing_container = {
            "config": {"url": "https://www.contoso.com/"},
        }
        with self.assertRaises(ValidationError):
            NotificationCreate.parse_obj(missing_container)


if __name__ == "__main__":
    unittest.main()
