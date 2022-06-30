#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.


import json
import unittest
from pathlib import Path

from onefuzztypes.models import Report

from __app__.onefuzzlib.reports import fix_report_size, parse_report_or_regression


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

    def test_report_no_resize(self) -> None:
        report_path = Path(__file__).parent / "data" / "report.json"
        with open(report_path, "r") as handle:
            content = handle.read()
            data = json.loads(content)
            report = Report.parse_obj(data)
            fixed_report = fix_report_size(content, report)
            self.assertEqual(report, fixed_report)

    def test_report_resize(self) -> None:
        report_path = Path(__file__).parent / "data" / "report-long.json"
        with open(report_path, "r") as handle:
            content = handle.read()
            data = json.loads(content)
            report = Report.parse_obj(data)
            fixed_report = fix_report_size(
                content, report, acceptable_report_length_kb=10, keep_num_entries=10
            )
            self.assertEqual(len(fixed_report.call_stack), 11)  # extra item is "..."
            report.call_stack = report.call_stack[0:10] + ["..."]
            self.assertEqual(report, fixed_report)


if __name__ == "__main__":
    unittest.main()
