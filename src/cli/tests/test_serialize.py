#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import unittest

from onefuzztypes.models import TeamsTemplate
from onefuzz.backend import serialize


class TestSerialize(unittest.TestCase):
    def test_cli_backend_secret_data_serialize(self) -> None:
        base = TeamsTemplate(url="https://contoso.com")
        converted = serialize(base)
        self.assertEqual(converted, {"url": "https://contoso.com"})

if __name__ == "__main__":
    unittest.main()