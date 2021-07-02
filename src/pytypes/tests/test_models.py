#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import unittest

from onefuzztypes.models import Scaleset, SecretData, TeamsTemplate
from onefuzztypes.primitives import PoolName, Region
from onefuzztypes.requests import NotificationCreate
from pydantic import ValidationError


class TestModelsVerify(unittest.TestCase):
    def test_model(self) -> None:
        data = {
            "container": "data",
            "config": {"url": {"secret": "https://www.contoso.com/"}},
        }

        notification = NotificationCreate.parse_obj(data)
        self.assertIsInstance(notification.config, TeamsTemplate)
        self.assertIsInstance(notification.config.url, SecretData)
        self.assertEqual(
            notification.config.url.secret,
            "https://www.contoso.com/",
            "mismatch secret value",
        )

        missing_container = {
            "config": {"url": "https://www.contoso.com/"},
        }
        with self.assertRaises(ValidationError):
            NotificationCreate.parse_obj(missing_container)

    def test_legacy_model(self) -> None:
        data = {
            "container": "data",
            "config": {"url": "https://www.contoso.com/"},
        }

        notification = NotificationCreate.parse_obj(data)
        self.assertIsInstance(notification.config, TeamsTemplate)
        self.assertIsInstance(notification.config.url, SecretData)

        missing_container = {
            "config": {"url": "https://www.contoso.com/"},
        }
        with self.assertRaises(ValidationError):
            NotificationCreate.parse_obj(missing_container)


class TestScaleset(unittest.TestCase):
    def test_scaleset_size(self) -> None:
        with self.assertRaises(ValueError):
            Scaleset(
                pool_name=PoolName("test-pool"),
                vm_sku="Standard_D2ds_v4",
                image="Canonical:UbuntuServer:18.04-LTS:latest",
                region=Region("westus2"),
                size=-1,
                spot_instances=False,
            )

        scaleset = Scaleset(
            pool_name=PoolName("test-pool"),
            vm_sku="Standard_D2ds_v4",
            image="Canonical:UbuntuServer:18.04-LTS:latest",
            region=Region("westus2"),
            size=0,
            spot_instances=False,
        )
        self.assertEqual(scaleset.size, 0)

        scaleset = Scaleset(
            pool_name=PoolName("test-pool"),
            vm_sku="Standard_D2ds_v4",
            image="Canonical:UbuntuServer:18.04-LTS:latest",
            region=Region("westus2"),
            size=80,
            spot_instances=False,
        )
        self.assertEqual(scaleset.size, 80)


if __name__ == "__main__":
    unittest.main()
