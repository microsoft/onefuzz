#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import unittest
from unittest.mock import MagicMock, patch
from uuid import UUID

from onefuzztypes.enums import OS, Architecture, ScalesetState
from onefuzztypes.models import AutoScaleConfig
from onefuzztypes.primitives import PoolName, Region

from __app__.onefuzzlib.workers.autoscale import (
    autoscale_pool,
    calc_scaleset_growth,
    calculate_change,
)
from __app__.onefuzzlib.workers.pools import Pool
from __app__.onefuzzlib.workers.scalesets import Scaleset

VM_SKU = "Standard_B2s"
IMAGE = "Canonical:UbuntuServer:18.04-LTS:latest"
REGION = Region("eastus")


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

    def test_scale_up(self) -> None:
        autoscale = AutoScaleConfig(image=IMAGE, vm_sku=VM_SKU)
        pool = Pool(
            name=PoolName("test-pool"),
            pool_id=UUID("6b049d51-23e9-4f5c-a5af-ff1f73d0d9e9"),
            os=OS.linux,
            managed=False,
            arch=Architecture.x86_64,
            autoscale=autoscale,
        )

        changes = calc_scaleset_growth(pool, [], 0)
        self.assertEqual(changes.existing, [])
        self.assertEqual(changes.new_scalesets, [])

        changes = calc_scaleset_growth(pool, [], 10)
        self.assertEqual(changes.existing, [])
        self.assertEqual(changes.new_scalesets, [10])

        changes = calc_scaleset_growth(pool, [], 1000)
        self.assertEqual(changes.existing, [])
        self.assertEqual(changes.new_scalesets, [1000])

        changes = calc_scaleset_growth(pool, [], 1010)
        self.assertEqual(changes.existing, [])
        self.assertEqual(changes.new_scalesets, [1000, 10])

        changes = calc_scaleset_growth(pool, [], 3010)
        self.assertEqual(changes.existing, [])
        self.assertEqual(changes.new_scalesets, [1000, 1000, 1000, 10])

        # scaleset_1 state is init, so it should never have changes
        scaleset_1 = Scaleset(
            pool_name=pool.name,
            vm_sku=VM_SKU,
            image=IMAGE,
            region=REGION,
            size=1,
            spot_instances=False,
        )

        # scaleset_2 state is running, but already at 1000 instances,
        # so should never have changes
        scaleset_2 = Scaleset(
            pool_name=pool.name,
            state=ScalesetState.running,
            vm_sku=VM_SKU,
            image=IMAGE,
            region=REGION,
            size=1000,
            spot_instances=False,
        )

        # these should always have modifications to scaleset_3 first,
        # then scaleset_4 regardless of order in the list
        scaleset_3 = Scaleset(
            scaleset_id=UUID(int=0),
            pool_name=pool.name,
            state=ScalesetState.running,
            vm_sku=VM_SKU,
            image=IMAGE,
            region=REGION,
            size=1,
            spot_instances=False,
        )
        scaleset_4 = Scaleset(
            scaleset_id=UUID(int=1),
            pool_name=pool.name,
            state=ScalesetState.running,
            vm_sku=VM_SKU,
            image=IMAGE,
            region=REGION,
            size=1,
            spot_instances=False,
        )

        changes = calc_scaleset_growth(pool, [scaleset_1, scaleset_2], 0)
        self.assertEqual(changes.existing, [])
        self.assertEqual(changes.new_scalesets, [])

        changes = calc_scaleset_growth(pool, [scaleset_1, scaleset_2], 10)
        self.assertEqual(changes.existing, [])
        self.assertEqual(changes.new_scalesets, [10])

        # verify we can grow existing scalesets
        changes = calc_scaleset_growth(
            pool, [scaleset_1, scaleset_2, scaleset_3, scaleset_4], 10
        )
        self.assertEqual(changes.existing, [(scaleset_3, 11)])
        self.assertEqual(changes.new_scalesets, [])

        # verify order doesn't matter
        changes = calc_scaleset_growth(
            pool, [scaleset_1, scaleset_2, scaleset_4, scaleset_3], 10
        )
        self.assertEqual(changes.existing, [(scaleset_3, 11)])
        self.assertEqual(changes.new_scalesets, [])

        # verify we can grow multiple scalesets and deal with left over correctly
        changes = calc_scaleset_growth(
            pool, [scaleset_1, scaleset_2, scaleset_4, scaleset_3], 3010
        )
        self.assertEqual(changes.existing, [(scaleset_3, 1000), (scaleset_4, 1000)])
        self.assertEqual(changes.new_scalesets, [1000, 12])

    def test_calculate_change(self) -> None:
        # no min size, no max size
        autoscale_1 = AutoScaleConfig(image=IMAGE, vm_sku=VM_SKU)
        pool = Pool(
            name=PoolName("test-pool"),
            pool_id=UUID("6b049d51-23e9-4f5c-a5af-ff1f73d0d9e9"),
            os=OS.linux,
            managed=False,
            arch=Architecture.x86_64,
            autoscale=autoscale_1,
        )

        # needed to make mypy happy later on
        assert pool.autoscale is not None

        scaleset_1 = Scaleset(
            pool_name=pool.name,
            vm_sku=VM_SKU,
            image=IMAGE,
            region=REGION,
            size=1,
            spot_instances=False,
            state=ScalesetState.running,
        )
        scaleset_2 = Scaleset(
            pool_name=pool.name,
            vm_sku=VM_SKU,
            image=IMAGE,
            region=REGION,
            size=2,
            spot_instances=False,
            state=ScalesetState.running,
        )

        # no scalesets, but need work
        change = calculate_change(pool, [], 0)
        self.assertEqual(change.change_size, 0)

        change = calculate_change(pool, [], 10)
        self.assertEqual(change.change_size, 10)

        # start with 3, end with 10
        change = calculate_change(pool, [scaleset_1, scaleset_2], 10)
        self.assertEqual(change.change_size, 7)

        # start with 3, end with 1
        change = calculate_change(pool, [scaleset_1, scaleset_2], 1)
        self.assertEqual(change.change_size, -2)

        # verify min_size
        pool.autoscale.min_size = 5

        change = calculate_change(pool, [scaleset_1, scaleset_2], 0)
        self.assertEqual(change.change_size, 2)

        change = calculate_change(pool, [scaleset_1, scaleset_2], 10)
        self.assertEqual(change.change_size, 7)

        # verify max_size
        pool.autoscale.max_size = 6
        change = calculate_change(pool, [scaleset_1, scaleset_2], 100)
        self.assertEqual(change.change_size, 3)
