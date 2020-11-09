#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import unittest
from unittest.mock import patch

from onefuzztypes.enums import OS, Architecture, TaskType
from onefuzztypes.models import (
    ContainerType,
    TaskConfig,
    TaskContainers,
    TaskDetails,
    TaskPool,
)

from __app__.onefuzzlib.autoscale import autoscale_pool, get_vm_count
from __app__.onefuzzlib.pools import Pool
from __app__.onefuzzlib.tasks.main import Task


class TestAutoscale(unittest.TestCase):
    def test_autoscale_pool(self):
        pool = Pool(
            name="test-pool",
            pool_id="6b049d51-23e9-4f5c-a5af-ff1f73d0d9e9",
            os=OS.linux,
            managed=False,
            arch=Architecture.x86_64,
        )
        self.assertIsNone(autoscale_pool(pool=pool))

    @patch("__app__.onefuzzlib.tasks.main.Task.get_pool")
    def test_get_vm_count(self, mock_get_pool):
        self.assertEqual(get_vm_count([]), 0)

        task_config = TaskConfig(
            job_id="6b049d51-23e9-4f5c-a5af-ff1f73d0d9e9",
            containers=[
                TaskContainers(type=ContainerType.inputs, name="test-container")
            ],
            tags={},
            task=TaskDetails(
                type=TaskType.libfuzzer_fuzz,
                duration=12,
                target_exe="fuzz.exe",
                target_env={},
                target_options=[],
            ),
            pool=TaskPool(count=2, pool_name="test-pool"),
        )
        task = Task(
            job_id="6b049d51-23e9-4f5c-a5af-ff1f73d0d9e9",
            os=OS.linux,
            config=task_config,
        )
        mock_get_pool.return_value = Pool(
            name="test-pool",
            pool_id="6b049d51-23e9-4f5c-a5af-ff1f73d0d9e9",
            os=OS.linux,
            managed=False,
            arch=Architecture.x86_64,
        )
        self.assertEqual(get_vm_count([task]), 2)
