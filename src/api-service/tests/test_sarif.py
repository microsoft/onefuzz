#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import unittest
from uuid import uuid4

from onefuzztypes.models import BlobRef, Report
from onefuzztypes.primitives import Container

from __app__.onefuzzlib.sarif import generate_sarif

# import logging


class TestSarif(unittest.TestCase):
    def test_basic(self) -> None:
        test_report = Report(
            input_url="https://test.com/test.exe",
            input_blob=BlobRef(
                account=str(uuid4()), container=Container("test"), name="test.exe"
            ),
            executable="fuzz.exe",
            crash_type="crash",
            crash_site="crash_site",
            call_stack=[],
            call_stack_sha256="call_stack_sha256",
            input_sha256="input_sha256",
            asan_log="asan_log",
            task_id=uuid4(),
            job_id=uuid4(),
            scariness_score=5,
            scariness_description="scariness_description",
            minimized_stack=[],
            minimized_stack_sha256="minimized_stack_sha256",
            minimized_stack_function_names=[],
            minimized_stack_function_names_sha256="test",
            minimized_stack_function_lines=[],
            minimized_stack_function_lines_sha256="test",
        )

        sarif = generate_sarif(test_report)

        print(f"sarif report : {sarif}")
