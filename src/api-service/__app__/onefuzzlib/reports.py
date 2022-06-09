#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
import logging
from sys import getsizeof
from typing import Optional, Union

from memoization import cached
from onefuzztypes.models import RegressionReport, Report
from onefuzztypes.primitives import Container
from pydantic import ValidationError

from .azure.containers import get_blob
from .azure.storage import StorageType


# This is fix for the following error:
# Exception while executing function:
# Functions.queue_file_changes Result: Failure
# Exception: AzureHttpError: Bad Request
# "The property value exceeds the maximum allowed size (64KB).
# If the property value is a string, it is UTF-16 encoded and
# the maximum number of characters should be 32K or less.
def fix_report_size(
    content: str,
    report: Report,
    acceptable_report_length_kb: int = 24,
    keep_num_entries: int = 10,
    keep_string_len: int = 256,
) -> Report:
    logging.info(f"report content length {getsizeof(content)}")
    if getsizeof(content) > acceptable_report_length_kb * 1024:
        msg = f"report data exceeds {acceptable_report_length_kb}K {getsizeof(content)}"
        if len(report.call_stack) > keep_num_entries:
            msg = msg + "; removing some of stack frames from the report"
            report.call_stack = report.call_stack[0:keep_num_entries] + ["..."]

        if report.asan_log and len(report.asan_log) > keep_string_len:
            msg = msg + "; removing some of asan log entries from the report"
            report.asan_log = report.asan_log[0:keep_string_len] + "..."

        if report.minimized_stack and len(report.minimized_stack) > keep_num_entries:
            msg = msg + "; removing some of minimized stack frames from the report"
            report.minimized_stack = report.minimized_stack[0:keep_num_entries] + [
                "..."
            ]

        if (
            report.minimized_stack_function_names
            and len(report.minimized_stack_function_names) > keep_num_entries
        ):
            msg = (
                msg
                + "; removing some of minimized stack function names from the report"
            )
            report.minimized_stack_function_names = (
                report.minimized_stack_function_names[0:keep_num_entries] + ["..."]
            )

        if (
            report.minimized_stack_function_lines
            and len(report.minimized_stack_function_lines) > keep_num_entries
        ):
            msg = (
                msg
                + "; removing some of minimized stack function lines from the report"
            )
            report.minimized_stack_function_lines = (
                report.minimized_stack_function_lines[0:keep_num_entries] + ["..."]
            )

        logging.info(msg)
    return report


def parse_report_or_regression(
    content: Union[str, bytes],
    file_path: Optional[str] = None,
    expect_reports: bool = False,
) -> Optional[Union[Report, RegressionReport]]:
    if isinstance(content, bytes):
        try:
            content = content.decode()
        except UnicodeDecodeError as err:
            if expect_reports:
                logging.error(
                    f"unable to parse report ({file_path}): "
                    f"unicode decode of report failed - {err}"
                )
            return None

    try:
        data = json.loads(content)
    except json.decoder.JSONDecodeError as err:
        if expect_reports:
            logging.error(
                f"unable to parse report ({file_path}): json decoding failed - {err}"
            )
        return None

    regression_err = None
    try:
        regression_report = RegressionReport.parse_obj(data)

        if (
            regression_report.crash_test_result is not None
            and regression_report.crash_test_result.crash_report is not None
        ):
            regression_report.crash_test_result.crash_report = fix_report_size(
                content, regression_report.crash_test_result.crash_report
            )

        if (
            regression_report.original_crash_test_result is not None
            and regression_report.original_crash_test_result.crash_report is not None
        ):
            regression_report.original_crash_test_result.crash_report = fix_report_size(
                content, regression_report.original_crash_test_result.crash_report
            )
        return regression_report
    except ValidationError as err:
        regression_err = err

    try:
        report = Report.parse_obj(data)
        return fix_report_size(content, report)
    except ValidationError as err:
        if expect_reports:
            logging.error(
                f"unable to parse report ({file_path}) as a report or regression. "
                f"regression error: {regression_err} report error: {err}"
            )
        return None


# cache the last 1000 reports
@cached(max_size=1000)
def get_report_or_regression(
    container: Container, filename: str, *, expect_reports: bool = False
) -> Optional[Union[Report, RegressionReport]]:
    file_path = "/".join([container, filename])
    if not filename.endswith(".json"):
        if expect_reports:
            logging.error("get_report invalid extension: %s", file_path)
        return None

    blob = get_blob(container, filename, StorageType.corpus)
    if blob is None:
        if expect_reports:
            logging.error("get_report invalid blob: %s", file_path)
        return None

    return parse_report_or_regression(
        blob, file_path=file_path, expect_reports=expect_reports
    )


def get_report(container: Container, filename: str) -> Optional[Report]:
    result = get_report_or_regression(container, filename)
    if isinstance(result, Report):
        return result
    return None
