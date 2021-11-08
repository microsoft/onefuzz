#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import argparse
import subprocess  # nosec
import sys
import time
import uuid
from azure.cli.core import get_default_cli

from onefuzz.api import Onefuzz
from uuid import UUID
import os
from onefuzztypes.models import ApiAccessRule

def az_cli(args):
    cli = get_default_cli()
    cli.logging_cls
    cli.invoke(args, out_file=open(os.devnull, "w"))
    if cli.result.result:
        return cli.result.result
    elif cli.result.error:
        raise cli.result.error


class APIRestrictionTests:
    def __init__(self, onefuzz_config_path=None):
        self.onefuzz = Onefuzz(config_path=onefuzz_config_path)
        self.intial_config = self.onefuzz.instance_config.get()

    def restore_config(self) -> None:
        self.onefuzz.instance_config.update(self.intial_config)

    def assign(self, group_id: UUID, member_id: UUID) -> None:
        instance_config = self.onefuzz.instance_config.get()
        if member_id in instance_config.group_membership:
            if group_id not in instance_config.group_membership[member_id]:
                instance_config.group_membership[member_id].append(group_id)

        self.onefuzz.instance_config.update(instance_config)


    def assign_current_user(self, group_id: UUID) -> None:
        onefuzz_service_appId = az_cli(
        [
            "ad",
            "signed-in-user",
            "show",
        ])
        member_id = UUID(onefuzz_service_appId["objectid"])
        self.assign(group_id, member_id)


    def test_restriction_on_current_user(self) -> None:

        print("Checking that the current user can get info")
        info = self.onefuzz.info.get()


        print("Creating test group")
        group_id = uuid.uuid4()

        print("Adding restriction to the info endpoint")
        instance_config = self.onefuzz.instance_config
        if instance_config.api_access_rules is None:
           instance_config.api_access_rules = []

        instance_config.api_access_rules.append(
            ApiAccessRule(
                path="/info",
                group_ids=[group_id],
                method="GET",
            )
        )
        self.onefuzz.instance_config.update(instance_config)

        print("Checking that the current user cannot get info")
        try:
            info = self.onefuzz.info.get()
            raise Exception("Expected exception not thrown")
        except Exception as e:
            pass

        print("Assigning current user to test group")
        self.assign_current_user(group_id)

        print("Checking that the current user can get info")
        info = self.onefuzz.info.get()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--config_path", default=None)
    args = parser.parse_args()
    tester = APIRestrictionTests(args.config_path)

    try:
        print("test current user restriction")
        tester.test_restriction_on_current_user()
    finally:
        tester.restore_config()



if __name__ == "__main__":
    main()
