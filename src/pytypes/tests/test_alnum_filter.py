#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import unittest

from onefuzztypes.validators import check_alnum_dash


class TestFilter(unittest.TestCase):
    def test_filter(self) -> None:
        check_alnum_dash("abc-")
        check_alnum_dash("-abc12A")

        invalid = [".", "abc'", "abc;", "abc\r", "abc\n", "abc;", "abc\x00"]
        for value in invalid:
            with self.assertRaises(ValueError):
                check_alnum_dash(value)


if __name__ == "__main__":
    unittest.main()
