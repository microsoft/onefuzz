#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.


import json
import os
import unittest
from pathlib import Path
from unittest.mock import patch

from onefuzztypes.enums import OS, TaskType
from onefuzztypes.models import ADOTemplate, JobConfig, Report, TaskConfig, TaskDetails
from onefuzztypes.primitives import Container

from __app__.onefuzzlib.jobs import Job
from __app__.onefuzzlib.notifications.ado import ADO
from __app__.onefuzzlib.notifications.common import Render
from __app__.onefuzzlib.tasks.main import Task


class TestReportParse(unittest.TestCase):
    def setUp(self):
        self.env_patch = patch.dict(
            "os.environ", {"ONEFUZZ_INSTANCE_NAME": "contoso-test"}
        )
        self.env_patch.start()

    def tearDown(self):
        self.env_patch.stop()

    def test_sample(self) -> None:
        expected_path = Path(__file__).parent / "data" / "ado-rendered.json"
        with open(expected_path, "r") as handle:
            expected_document = json.load(handle)

        report_path = Path(__file__).parent / "data" / "crash-report-with-html.json"
        with open(report_path, "r") as handle:
            report_raw = json.load(handle)

        ado_path = Path(__file__).parent / "data" / "ado-config.json"
        with open(ado_path, "r") as handle:
            ado_raw = json.load(handle)

        report = Report.parse_obj(report_raw)
        config = ADOTemplate.parse_obj(ado_raw)

        container = Container("containername")
        filename = "test.json"

        job = Job(
            config=JobConfig(project="project", name="name", build="build", duration=1)
        )
        task = Task(
            config=TaskConfig(
                job_id=job.job_id,
                tags={},
                containers=[],
                task=TaskDetails(type=TaskType.libfuzzer_fuzz, duration=1),
            ),
            job_id=job.job_id,
            os=OS.linux,
        )

        renderer = Render(
            container,
            filename,
            report,
            task=task,
            job=job,
            target_url="https://contoso.com/1",
            input_url="https://contoso.com/2",
            report_url="https://contoso.com/3",
        )

        ado = ADO(container, filename, config, report, renderer=renderer)
        work_item_type, document = ado.render_new()
        self.assertEqual(work_item_type, "Bug")

        as_obj = [x.as_dict() for x in document]

        self.assertEqual(as_obj, expected_document)


if __name__ == "__main__":
    unittest.main()
