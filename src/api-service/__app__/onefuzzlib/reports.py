#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
import logging
from typing import Optional, Union

from memoization import cached
from onefuzztypes.models import RegressionReport, Report
from onefuzztypes.primitives import Container
from pydantic import ValidationError

from .azure.containers import get_blob
from .azure.storage import StorageType


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
        return RegressionReport.parse_obj(data)
    except ValidationError as err:
        regression_err = err

    try:
        return Report.parse_obj(data)
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
