#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import unittest

from pydantic import ValidationError

from onefuzztypes.models import Scaleset, TeamsTemplate
from onefuzztypes.requests import NotificationCreate


class TestModelsVerify(unittest.TestCase):
    def test_model(self) -> None:
        data = {
            "container": "data",
            "config": {"url": "https://www.contoso.com/"},
        }

        notification = NotificationCreate.parse_obj(data)
        self.assertIsInstance(notification.config, TeamsTemplate)

        missing_container = {
            "config": {"url": "https://www.contoso.com/"},
        }
        with self.assertRaises(ValidationError):
            NotificationCreate.parse_obj(missing_container)


class TestScaleset(unittest.TestCase):
    def test_scaleset_size(self) -> None:
        with self.assertRaises(ValueError):
            Scaleset(
                pool_name="test_pool",
                vm_sku="Standard_D2ds_v4",
                image="Canonical:UbuntuServer:18.04-LTS:latest",
                region="westus2",
                size=-1,
                spot_instances=False,
            )

        scaleset = Scaleset(
            pool_name="test_pool",
            vm_sku="Standard_D2ds_v4",
            image="Canonical:UbuntuServer:18.04-LTS:latest",
            region="westus2",
            size=0,
            spot_instances=False,
        )
        self.assertEqual(scaleset.size, 0)

        scaleset = Scaleset(
            pool_name="test_pool",
            vm_sku="Standard_D2ds_v4",
            image="Canonical:UbuntuServer:18.04-LTS:latest",
            region="westus2",
            size=80,
            spot_instances=False,
        )
        self.assertEqual(scaleset.size, 80)


if __name__ == "__main__":
    unittest.main()
