#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import argparse
import subprocess  # nosec
import sys
import time
import uuid
import json
import unittest


from deployment.configuration import NetworkSecurityConfig
from onefuzztypes.enums import OS, ScalesetState
from typing import Any, List, Tuple
from pydantic import BaseModel

# DeployTestCase = Tuple[Any, bool]

# DEPLOY_CONFIG_TEST_CASES = [
#     # Valid
#     ({}, True)
#     ("", False)
# ]

class TestNetworkSecurityConfig(BaseModel):
    allowed_ips: List[str]
    allowed_service_tags: List[str]

class DeployTests(unittest.TestCase):

    def test_config(self) -> None:
        
        valid_config = {} 
        self.assertIsInstance(TestNetworkSecurityConfig, NetworkSecurityConfig(valid_config))


# def main() -> None:
    
#     # with open(args.config, "r") as template_handle:
#     #     config_template = json.load(template_handle)

#     t = DeployTests()
    
#     print("Test Dictionary")
#     t.test_invalid_config("")
#     t.test_valid_config({})
#     # t.test_nsg_json()
#     # print("Test basic proxy access")
#     # t.test_proxy_access(pool_name, args.region1)
#     # print("Test new region addition access")
#     # t.test_new_scaleset_region(pool_name, args.region1, args.region2)
#     # print("Test proxy cycle")
#     # t.test_proxy_cycle(pool_name, args.region1)


if __name__ == "__main__":
    unittest.main()
