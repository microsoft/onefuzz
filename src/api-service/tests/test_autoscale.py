#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import unittest
from unittest.mock import MagicMock, patch
from uuid import UUID

from onefuzztypes.enums import OS, Architecture
from onefuzztypes.primitives import PoolName

from __app__.onefuzzlib.workers.autoscale import autoscale_pool
from __app__.onefuzzlib.workers.pools import Pool


class TestAutoscale(unittest.TestCase):
    @patch("__app__.onefuzzlib.workers.autoscale.needed_nodes")
    def test_autoscale_pool(self, needed_nodes: MagicMock) -> None:
        pool = Pool(
            name=PoolName("test-pool"),
            pool_id=UUID("6b049d51-23e9-4f5c-a5af-ff1f73d0d9e9"),
            os=OS.linux,
            managed=False,
            arch=Architecture.x86_64,
        )
        autoscale_pool(pool=pool)
        needed_nodes.assert_not_called()
