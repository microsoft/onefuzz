#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import argparse
import subprocess  # nosec
import sys
import time
import uuid

from onefuzz.api import Onefuzz
from onefuzztypes.enums import OS, ScalesetState


class NsgTests:
    def __init__(self):
        self.onefuzz = Onefuzz()

    def allow_all(self) -> None:
        instance_config = self.onefuzz.instance_config.get()
        instance_config.proxy_nsg_config.allowed_ips = ["*"]
        self.onefuzz.instance_config.update(instance_config)

    def block_all(self) -> None:
        instance_config = self.onefuzz.instance_config.get()
        instance_config.proxy_nsg_config.allowed_ips = []
        self.onefuzz.instance_config.update(instance_config)

    def generate_name(self) -> str:
        return "nsg-test-%s" % uuid.uuid4().hex

    def get_proxy_ip(self, scaleset: ScalesetState) -> str:
        machine_id = scaleset.nodes[0].machine_id
        scaleset_id = scaleset.scaleset_id
        timeout_seconds = 60
        time_waited = 0
        wait_poll = 5

        ip = None
        print("Retrieving proxy IP for region: %s" % scaleset.region)

        while time_waited < timeout_seconds and ip is None:
            proxy = self.onefuzz.scaleset_proxy.create(
                scaleset_id, machine_id, dst_port=1
            )
            ip = proxy.ip
            if ip is None:
                time.sleep(wait_poll)

        if ip is None:
            raise Exception("Failed to get proxy IP for region: %s" % scaleset.region)
        return ip

    def test_connection(self, ip: str) -> bool:
        cmd = ["ping", ip]
        if sys.platform == "linux":
            cmd.append("-w")
            cmd.append("5")
        return 0 == subprocess.call(cmd)  # nosec

    def wait_for_scaleset(self, scaleset: ScalesetState) -> ScalesetState:
        timeout_seconds = 600
        wait_poll = 10
        # wait for scaleset creation to finish
        time_waited = 0
        tmp_scaleset = scaleset
        while (
            time_waited < timeout_seconds
            and tmp_scaleset.error is None
            and tmp_scaleset.state != ScalesetState.running
        ):
            tmp_scaleset = self.onefuzz.scalesets.get(tmp_scaleset.scaleset_id)
            print(
                "Waiting for scaleset creation... Current scaleset state: %s"
                % tmp_scaleset.state
            )
            time.sleep(wait_poll)
            time_waited = time_waited + wait_poll

        if tmp_scaleset.error:
            raise Exception(
                "Failed to provision scaleset %s" % (tmp_scaleset.scaleset_id)
            )

        return tmp_scaleset

    def create_pool(self, pool_name):
        self.onefuzz.pools.create(pool_name, OS.linux)

    def create_scaleset(self, pool_name: str, region: str) -> ScalesetState:
        scaleset = self.onefuzz.scalesets.create(pool_name, 1, region=region)
        return self.wait_for_scaleset(scaleset)

    def wait_for_nsg_rules_to_apply(self) -> None:
        time.sleep(120)

    def test_proxy_access(self, pool_name: str, region: str) -> None:
        scaleset = self.create_scaleset(pool_name, region)
        try:
            ip = self.get_proxy_ip(scaleset)
            print("Allow connection")

            self.allow_all()
            self.wait_for_nsg_rules_to_apply()
            # can ping since all allowed
            result = self.test_connection(ip)
            if not result:
                raise Exception("Failed to connect to proxy")

            print("Block connection")
            self.block_all()
            self.wait_for_nsg_rules_to_apply()
            # should not be able to ping since all blocked
            result = self.test_connection(ip)
            if result:
                raise Exception("Connected to proxy")

            print("Allow connection")
            self.allow_all()
            self.wait_for_nsg_rules_to_apply()
            # can ping since all allowed
            result = self.test_connection(ip)
            if not result:
                raise Exception("Failed to connect to proxy")

            print("Block connection")
            self.block_all()
            self.wait_for_nsg_rules_to_apply()
            # should not be able to ping since all blocked
            result = self.test_connection(ip)
            if result:
                raise Exception("Connected to proxy")
        finally:
            self.onefuzz.scalesets.shutdown(scaleset.scaleset_id, now=True)

    def test_new_scaleset_region(
        self, pool_name: str, region1: str, region2: str
    ) -> None:
        if region1 == region2:
            raise Exception(
                (
                    "Test input parameter validation failure.",
                    " Scalesets expted to be in different regions",
                )
            )

        scaleset1 = self.create_scaleset(pool_name, region1)
        scaleset2 = None
        try:
            ip1 = self.get_proxy_ip(scaleset1)

            print("Block connection")
            self.block_all()
            self.wait_for_nsg_rules_to_apply()
            # should not be able to ping since all blocked
            print("Attempting connection for region %s" % region1)
            result = self.test_connection(ip1)
            if result:
                raise Exception("Connected to proxy1 in region %s" % region1)

            print("Allow connection")
            self.allow_all()
            self.wait_for_nsg_rules_to_apply()
            # can ping since all allowed
            print("Attempting connection for region %s" % region1)
            result = self.test_connection(ip1)
            if not result:
                raise Exception("Failed to connect to proxy1 in region %s" % region1)

            print("Creating scaleset in region %s" % region2)
            scaleset2 = self.create_scaleset(pool_name, region2)

            ip2 = self.get_proxy_ip(scaleset2)
            # should not be able to ping since all blocked
            print("Attempting connection for region %s" % region2)
            result = self.test_connection(ip2)
            if not result:
                raise Exception("Failed to connect to proxy2 in region %s" % region2)

            print("Block connection")
            self.block_all()
            self.wait_for_nsg_rules_to_apply()
            # should not be able to ping since all blocked
            print("Attempting connection for region %s" % region1)
            result = self.test_connection(ip1)
            if result:
                raise Exception("Connected to proxy1 in region" % region1)

            # should not be able to ping since all blocked
            print("Attempting connection for region %s" % region2)
            result = self.test_connection(ip2)
            if result:
                raise Exception("Connected to proxy2 in region %s" % region2)

        finally:
            self.onefuzz.scalesets.shutdown(scaleset1.scaleset_id, now=True)

            if scaleset2:
                self.onefuzz.scalesets.shutdown(scaleset2.scaleset_id, now=True)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--region1")
    parser.add_argument("--region2")
    args = parser.parse_args()

    if not args.region1 or not args.region2:
        raise Exception("--region1 and --region2 are required")

    t = NsgTests()
    pool_name = t.generate_name()
    t.create_pool(pool_name)
    print("Test basic proxy access")
    t.test_proxy_access(pool_name, args.region1)
    print("Test new region addition access")
    t.test_new_scaleset_region(pool_name, args.region1, args.region2)


if __name__ == "__main__":
    main()
