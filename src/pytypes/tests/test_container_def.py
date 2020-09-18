#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import unittest

from onefuzztypes.enums import ContainerType
from onefuzztypes.models import TaskUnitConfig


class TestModelsVerify(unittest.TestCase):
    def test_container_type_setup(self) -> None:
        for item in ContainerType:
            # setup container is explicitly ignored for now
            if item == ContainerType.setup:
                continue

            self.assertIn(item.name, TaskUnitConfig.__fields__)


if __name__ == "__main__":
    unittest.main()
