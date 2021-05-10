#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.


import json
import unittest
from pathlib import Path

from onefuzztypes.models import Report

from __app__.onefuzzlib.reports import parse_report_or_regression


class TestReportParse(unittest.TestCase):
    def test_sample(self) -> None:
        report_path = Path(__file__).parent / "data" / "report.json"
        with open(report_path, "r") as handle:
            data = json.load(handle)

        invalid = {"unused_field_1": 3}
        report = parse_report_or_regression(json.dumps(data))
        self.assertIsInstance(report, Report)

        with self.assertLogs(level="ERROR"):
            self.assertIsNone(
                parse_report_or_regression('"invalid"', expect_reports=True)
            )

        with self.assertLogs(level="WARNING") as logs:
            self.assertIsNone(
                parse_report_or_regression(json.dumps(invalid), expect_reports=True)
            )
            self.assertTrue(any(["unable to parse report" in x for x in logs.output]))


if __name__ == "__main__":
    unittest.main()
