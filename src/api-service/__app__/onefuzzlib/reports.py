#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
import logging
from typing import Optional, Union

from memoization import cached
from onefuzztypes.models import Report
from onefuzztypes.primitives import Container
from pydantic import ValidationError

from .azure.containers import get_blob
from .azure.storage import StorageType


def parse_report(
    content: Union[str, bytes], file_path: Optional[str] = None
) -> Optional[Report]:
    if isinstance(content, bytes):
        try:
            content = content.decode()
        except UnicodeDecodeError as err:
            logging.error(
                "unable to parse report (%s): unicode decode of report failed - %s",
                file_path,
                err,
            )
            return None

    try:
        data = json.loads(content)
    except json.decoder.JSONDecodeError as err:
        logging.error(
            "unable to parse report (%s): json decoding failed - %s", file_path, err
        )
        return None

    try:
        entry = Report.parse_obj(data)
    except ValidationError as err:
        logging.error("unable to parse report (%s): %s", file_path, err)
        return None

    return entry


# cache the last 1000 reports
@cached(max_size=1000)
def get_report(container: Container, filename: str) -> Optional[Report]:
    file_path = "/".join([container, filename])
    if not filename.endswith(".json"):
        logging.error("get_report invalid extension: %s", file_path)
        return None

    blob = get_blob(container, filename, StorageType.corpus)
    if blob is None:
        logging.error("get_report invalid blob: %s", file_path)
        return None

    return parse_report(blob, file_path=file_path)
