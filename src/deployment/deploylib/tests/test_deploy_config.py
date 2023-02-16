#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import unittest
from typing import Any

from deploylib.configuration import Config


class DeployTests(unittest.TestCase):
    def test_config(self) -> None:
        ## Test Invalid Configs
        #  Test Dictionary
        invalid_config: Any = ""
        with self.assertRaises(Exception):
            Config(invalid_config)
        #  Test Empty Dic
        invalid_config = {}
        with self.assertRaises(Exception):
            Config(invalid_config)
        #  Test Invalid Outer Keys
        invalid_config = {"": ""}
        with self.assertRaises(Exception):
            Config(invalid_config)
        #  Test Inner Dictionary
        invalid_config = {"proxy_nsg_config": ""}
        with self.assertRaises(Exception):
            Config(invalid_config)
        #  Test Inner Keys
        invalid_config = {"proxy_nsg_config": {}}
        with self.assertRaises(Exception):
            Config(invalid_config)
        #  Test Inner Keys
        invalid_config = {"proxy_nsg_config": {"allowed_ips": ""}}
        with self.assertRaises(Exception):
            Config(invalid_config)
        #  Test Inner Dict Values (lists)
        invalid_config = {
            "proxy_nsg_config": {"allowed_ips": [], "allowed_service_tags": ""}
        }
        with self.assertRaises(Exception):
            Config(invalid_config)
        #  Test List Values
        invalid_config = {
            "proxy_nsg_config": {
                "allowed_ips": [1, 2],
                "allowed_service_tags": ["10.0.0.0"],
            }
        }
        with self.assertRaises(Exception):
            Config(invalid_config)

        ## Test Valid Configs
        #  Test Empty Lists
        valid_config: Any = {
            "proxy_nsg_config": {"allowed_ips": [], "allowed_service_tags": []}
        }
        Config(valid_config)
        #  Test Wild Card Lists
        valid_config = {
            "proxy_nsg_config": {"allowed_ips": ["*"], "allowed_service_tags": []}
        }
        Config(valid_config)
        #  Test IPs Lists
        valid_config = {
            "proxy_nsg_config": {
                "allowed_ips": ["10.0.0.1", "10.0.0.2"],
                "allowed_service_tags": [],
            }
        }
        Config(valid_config)
        #  Test Tags Lists
        valid_config = {
            "proxy_nsg_config": {
                "allowed_ips": ["10.0.0.1", "10.0.0.2"],
                "allowed_service_tags": ["Internet"],
            }
        }
        Config(valid_config)


if __name__ == "__main__":
    unittest.main()
