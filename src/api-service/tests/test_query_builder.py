#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import unittest
from typing import Optional, Tuple
from uuid import UUID

from __app__.onefuzzlib.orm import ORMMixin, build_filters


class TestOrm(ORMMixin):
    a: int
    b: UUID
    c: str
    d: int
    e: int

    @classmethod
    def key_fields(cls) -> Tuple[str, Optional[str]]:
        return ("d", "e")


class TestQueryBuilder(unittest.TestCase):
    def test_filter(self) -> None:
        self.maxDiff = 999999999999999

        self.assertEqual(
            build_filters(TestOrm, {"a": [1]}), ("a eq 1", {}), "handle integer"
        )
        self.assertEqual(
            build_filters(
                TestOrm, {"b": [UUID("06aa1e71-b025-4325-9983-4b3ce2de12ea")]}
            ),
            ("b eq '06aa1e71-b025-4325-9983-4b3ce2de12ea'", {}),
            "handle UUID",
        )
        self.assertEqual(
            build_filters(TestOrm, {"a": ["a"]}), (None, {"a": ["a"]}), "handle str"
        )

        self.assertEqual(
            build_filters(TestOrm, {"a": [1, 2]}),
            ("(a eq 1 or a eq 2)", {}),
            "multiple values",
        )
        self.assertEqual(
            build_filters(TestOrm, {"a": ["b"], "c": ["d"]}),
            (None, {"a": ["b"], "c": ["d"]}),
            "multiple fields",
        )
        self.assertEqual(
            build_filters(TestOrm, {"a": [1, 2], "c": [3]}),
            ("(a eq 1 or a eq 2) and c eq 3", {}),
            "multiple fields and values",
        )

        self.assertEqual(
            build_filters(
                TestOrm,
                {
                    "a": ["b"],
                    "b": [1],
                    "c": [UUID("06aa1e71-b025-4325-9983-4b3ce2de12ea")],
                },
            ),
            ("b eq 1 and c eq '06aa1e71-b025-4325-9983-4b3ce2de12ea'", {"a": ["b"]}),
            "multiple fields, types, and values",
        )

        self.assertEqual(
            build_filters(TestOrm, {"d": [1, 2], "e": [3]}),
            ("(PartitionKey eq 1 or PartitionKey eq 2) and RowKey eq 3", {}),
            "query on keyfields",
        )

        with self.assertRaises(ValueError):
            build_filters(TestOrm, {"test1": ["b", "c"], "test2": ["d"]})


if __name__ == "__main__":
    unittest.main()
