#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import unittest
import sys
from typing import List

from pydantic import BaseModel

from deploylib.configuration import NetworkSecurityConfig


class TestNetworkSecurityConfig(BaseModel):
    allowed_ips: List[str]
    allowed_service_tags: List[str]


class DeployTests(unittest.TestCase):
    def test_config(self) -> None:
        ## Test Invalid Configs
        #  Test Dictionary
        invalid_config = ""
        with self.assertRaises(Exception):
            NetworkSecurityConfig(invalid_config)
        #  Test Empty Dic
        invalid_config = {}
        with self.assertRaises(Exception):
            NetworkSecurityConfig(invalid_config)
        #  Test Invalid Outer Keys
        invalid_config = {"": ""}
        with self.assertRaises(Exception):
            NetworkSecurityConfig(invalid_config)
        #  Test Inner Dictionary
        invalid_config = {"proxy_nsg_config": ""}
        with self.assertRaises(Exception):
            NetworkSecurityConfig(invalid_config)
        #  Test Inner Keys
        invalid_config = {"proxy_nsg_config": {}}
        with self.assertRaises(Exception):
            NetworkSecurityConfig(invalid_config)
        #  Test Inner Keys
        invalid_config = {"proxy_nsg_config": {"allowed_ips": ""}}
        with self.assertRaises(Exception):
            NetworkSecurityConfig(invalid_config)
        #  Test Inner Dict Values (lists)
        invalid_config = {
            "proxy_nsg_config": {"allowed_ips": [], "allowed_service_tags": ""}
        }
        with self.assertRaises(Exception):
            NetworkSecurityConfig(invalid_config)
        #  Test List Values
        invalid_config = {
            "proxy_nsg_config": {
                "allowed_ips": [1, 2],
                "allowed_service_tags": ["10.0.0.0"],
            }
        }
        with self.assertRaises(Exception):
            NetworkSecurityConfig(invalid_config)

        ## Test Valid Configs
        #  Test Empty Lists
        valid_config = {
            "proxy_nsg_config": {"allowed_ips": [], "allowed_service_tags": []}
        }
        NetworkSecurityConfig(valid_config)
        #  Test Wild Card Lists
        valid_config = {
            "proxy_nsg_config": {"allowed_ips": ["*"], "allowed_service_tags": []}
        }
        NetworkSecurityConfig(valid_config)
        #  Test IPs Lists
        valid_config = {
            "proxy_nsg_config": {
                "allowed_ips": ["10.0.0.1", "10.0.0.2"],
                "allowed_service_tags": [],
            }
        }
        NetworkSecurityConfig(valid_config)
        #  Test Tags Lists
        valid_config = {
            "proxy_nsg_config": {
                "allowed_ips": ["10.0.0.1", "10.0.0.2"],
                "allowed_service_tags": ["Internet"],
            }
        }
        NetworkSecurityConfig(valid_config)


if __name__ == "__main__":
    unittest.main()

