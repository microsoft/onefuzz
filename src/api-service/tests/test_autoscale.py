#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import unittest
from unittest.mock import MagicMock, patch
from uuid import UUID

from onefuzztypes.enums import OS, Architecture, ContainerType, TaskType
from onefuzztypes.models import (TaskConfig, TaskContainers, TaskDetails,
                                 TaskPool)
from onefuzztypes.primitives import Container, PoolName

from __app__.onefuzzlib.autoscale import autoscale_pool, get_vm_count
from __app__.onefuzzlib.tasks.main import Task
from __app__.onefuzzlib.workers.pools import Pool


class TestAutoscale(unittest.TestCase):
    @patch("__app__.onefuzzlib.tasks.main.Task.get_tasks_by_pool_name")
    def test_autoscale_pool(self, mock_get_tasks_by_pool_name: MagicMock) -> None:
        pool = Pool(
            name=PoolName("test-pool"),
            pool_id=UUID("6b049d51-23e9-4f5c-a5af-ff1f73d0d9e9"),
            os=OS.linux,
            managed=False,
            arch=Architecture.x86_64,
        )
        autoscale_pool(pool=pool)
        mock_get_tasks_by_pool_name.assert_not_called()

    @patch("__app__.onefuzzlib.tasks.main.Task.get_pool")
    def test_get_vm_count(self, mock_get_pool: MagicMock) -> None:
        self.assertEqual(get_vm_count([]), 0)

        task_config = TaskConfig(
            job_id=UUID("6b049d51-23e9-4f5c-a5af-ff1f73d0d9e9"),
            containers=[
                TaskContainers(
                    type=ContainerType.inputs, name=Container("test-container")
                )
            ],
            tags={},
            task=TaskDetails(
                type=TaskType.libfuzzer_fuzz,
                duration=12,
                target_exe="fuzz.exe",
                target_env={},
                target_options=[],
            ),
            pool=TaskPool(count=2, pool_name=PoolName("test-pool")),
        )
        task = Task(
            job_id=UUID("6b049d51-23e9-4f5c-a5af-ff1f73d0d9e9"),
            os=OS.linux,
            config=task_config,
        )
        mock_get_pool.return_value = Pool(
            name=PoolName("test-pool"),
            pool_id=UUID("6b049d51-23e9-4f5c-a5af-ff1f73d0d9e9"),
            os=OS.linux,
            managed=False,
            arch=Architecture.x86_64,
        )
        self.assertEqual(get_vm_count([task]), 2)
