#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import unittest

from onefuzztypes.enums import (
    Compare,
    ContainerPermission,
    ContainerType,
    TaskFeature,
    TaskType,
)
from onefuzztypes.models import ContainerDefinition, TaskDefinition, VmDefinition

from __app__.onefuzzlib.tasks.defs import TASK_DEFINITIONS


class TestTaskDefinition(unittest.TestCase):
    def test_definitions(self) -> None:
        for entry in TASK_DEFINITIONS.values():
            self.assertIsInstance(entry, TaskDefinition)
            TaskDefinition.validate(entry)

    def test_all_defined(self) -> None:
        for entry in [TaskType[x] for x in TaskType.__members__]:
            if entry == TaskType.libfuzzer_coverage:
                # Deprecated, kept in enum for deserialization back-compat.
                continue

            self.assertIn(entry, TASK_DEFINITIONS)

    def test_basic(self) -> None:
        task_def_from_py = TaskDefinition(
            features=[TaskFeature.target_exe, TaskFeature.target_env],
            containers=[
                ContainerDefinition(
                    type=ContainerType.inputs,
                    compare=Compare.AtLeast,
                    value=1,
                    permissions=[ContainerPermission.Read, ContainerPermission.Write],
                )
            ],
            monitor_queue=None,
            vm=VmDefinition(compare=Compare.AtLeast, value=1),
        )
        TaskDefinition.validate(task_def_from_py)


if __name__ == "__main__":
    unittest.main()
