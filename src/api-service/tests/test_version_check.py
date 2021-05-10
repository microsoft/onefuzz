#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import unittest

from __app__.onefuzzlib.versions import is_minimum_version


class TestMinVersion(unittest.TestCase):
    def test_basic(self) -> None:
        self.assertEqual(is_minimum_version(version="1.0.0", minimum="1.0.0"), True)
        self.assertEqual(is_minimum_version(version="2.0.0", minimum="1.0.0"), True)
        self.assertEqual(is_minimum_version(version="2.0.0", minimum="3.0.0"), False)
        self.assertEqual(is_minimum_version(version="1.0.0", minimum="1.6.0"), False)


if __name__ == "__main__":
    unittest.main()
