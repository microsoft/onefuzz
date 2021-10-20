#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from typing import Set, Tuple

import pytest
from onefuzztypes.primitives import Region

from __app__.onefuzzlib.azure.nsg import ok_to_delete

# Active regions, NSG region, NSG name, expected result
NsgOkToDeleteTestCase = Tuple[Set[Region], str, str, bool]

NSG_OK_TO_DELETE_TEST_CASES = [
    # OK to delete
    (set([Region("def"), Region("ghk")]), "abc", "abc", True),
    # Not OK to delete
    # region set has same region as NSG
    (set([Region("abc"), Region("def"), Region("ghk")]), "abc", "abc", False),
    # NSG region does not match it's name
    (set([Region("abc"), Region("def"), Region("ghk")]), "abc", "cba", False),
    (set([Region("def"), Region("ghk")]), "abc", "cba", False),
]


@pytest.mark.parametrize("nsg_ok_to_delete_test_case", NSG_OK_TO_DELETE_TEST_CASES)
def test_is_ok_to_delete_nsg(nsg_ok_to_delete_test_case: NsgOkToDeleteTestCase) -> None:
    regions, nsg_location, nsg_name, expected = nsg_ok_to_delete_test_case
    result = ok_to_delete(regions, nsg_location, nsg_name)
    assert result == expected
