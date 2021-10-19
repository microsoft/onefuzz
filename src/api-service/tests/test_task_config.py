#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import pytest

from __app__.onefuzzlib.tasks.config import is_valid_blob_name


BLOB_NAME_TEST_CASES = [
    # Valid
    ("fuzz.exe", True),
    ("bin/fuzz.exe", True),
    ("/".join("a" * 254), True),
    ("a" * 1024, True),

    # Invalid (absolute)
    ("/fuzz.exe", False),
    ("/bin/fuzz.exe", False),

    # Invalid (special dirs)
    ("./fuzz.exe", False),
    ("././fuzz.exe", False),
    ("./bin/fuzz.exe", False),
    ("./bin/./fuzz.exe", False),
    ("../fuzz.exe", False),
    ("../bin/fuzz.exe", False),
    (".././fuzz.exe", False),
    ("../bin/./fuzz.exe", False),

    # Out of Azure size bounds
    ("", False),
    ("  ", False),
    ("/".join("a" * 255), False),
    ("a" * 1025, False),
]


@pytest.mark.parametrize("blob_name_test_case", BLOB_NAME_TEST_CASES)
def test_is_valid_blob_name(blob_name_test_case):
    blob_name, expected = blob_name_test_case

    is_valid = is_valid_blob_name(blob_name)

    assert is_valid == expected
