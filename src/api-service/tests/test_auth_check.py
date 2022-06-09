#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import os
import unittest
from uuid import UUID, uuid4

from onefuzztypes.models import UserInfo

from __app__.onefuzzlib.config import InstanceConfig
from __app__.onefuzzlib.endpoint_authorization import (
    can_modify_config_impl,
    check_require_admins_impl,
)

if "ONEFUZZ_INSTANCE_NAME" not in os.environ:
    os.environ["ONEFUZZ_INSTANCE_NAME"] = "test"


class TestAdmin(unittest.TestCase):
    def test_modify_config(self) -> None:
        user1 = uuid4()
        user2 = uuid4()

        # no admins set
        self.assertTrue(
            can_modify_config_impl(
                InstanceConfig(allowed_aad_tenants=[UUID(int=0)]), UserInfo()
            )
        )

        # with oid, but no admin
        self.assertTrue(
            can_modify_config_impl(
                InstanceConfig(allowed_aad_tenants=[UUID(int=0)]),
                UserInfo(object_id=user1),
            )
        )

        # is admin
        self.assertTrue(
            can_modify_config_impl(
                InstanceConfig(allowed_aad_tenants=[UUID(int=0)], admins=[user1]),
                UserInfo(object_id=user1),
            )
        )

        # no user oid set
        self.assertFalse(
            can_modify_config_impl(
                InstanceConfig(allowed_aad_tenants=[UUID(int=0)], admins=[user1]),
                UserInfo(),
            )
        )

        # not an admin
        self.assertFalse(
            can_modify_config_impl(
                InstanceConfig(allowed_aad_tenants=[UUID(int=0)], admins=[user1]),
                UserInfo(object_id=user2),
            )
        )

    def test_manage_pools(self) -> None:
        user1 = uuid4()
        user2 = uuid4()

        # by default, any can modify
        self.assertIsNone(
            check_require_admins_impl(
                InstanceConfig(
                    allowed_aad_tenants=[UUID(int=0)], require_admin_privileges=False
                ),
                UserInfo(),
            )
        )

        # with oid, but no admin
        self.assertIsNone(
            check_require_admins_impl(
                InstanceConfig(
                    allowed_aad_tenants=[UUID(int=0)], require_admin_privileges=False
                ),
                UserInfo(object_id=user1),
            )
        )

        # is admin
        self.assertIsNone(
            check_require_admins_impl(
                InstanceConfig(
                    allowed_aad_tenants=[UUID(int=0)],
                    require_admin_privileges=True,
                    admins=[user1],
                ),
                UserInfo(object_id=user1),
            )
        )

        # no user oid set
        self.assertIsNotNone(
            check_require_admins_impl(
                InstanceConfig(
                    allowed_aad_tenants=[UUID(int=0)],
                    require_admin_privileges=True,
                    admins=[user1],
                ),
                UserInfo(),
            )
        )

        # not an admin
        self.assertIsNotNone(
            check_require_admins_impl(
                InstanceConfig(
                    allowed_aad_tenants=[UUID(int=0)],
                    require_admin_privileges=True,
                    admins=[user1],
                ),
                UserInfo(object_id=user2),
            )
        )


if __name__ == "__main__":
    unittest.main()
