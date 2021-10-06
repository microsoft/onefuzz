#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from uuid import UUID
import unittest

from onefuzztypes.models import InstanceConfig


class TestInstanceConfig(unittest.TestCase):
    def test_with_admins(self) -> None:
        no_admins = InstanceConfig(admins=None, allowed_aad_tenants=[UUID(int=0)])
        with_admins = InstanceConfig(
            admins=[UUID(int=0)], allowed_aad_tenants=[UUID(int=0)]
        )
        with_admins_2 = InstanceConfig(
            admins=[UUID(int=1)], allowed_aad_tenants=[UUID(int=0)]
        )

        no_admins.update(with_admins)
        self.assertEqual(no_admins.admins, None)

        with_admins.update(with_admins_2)
        self.assertEqual(with_admins.admins, with_admins_2.admins)

    def test_with_empty_admins(self) -> None:
        with self.assertRaises(ValueError):
            InstanceConfig.parse_obj({"admins": []})


if __name__ == "__main__":
    unittest.main()
