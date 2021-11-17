#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import argparse
import os
from urllib.parse import urlparse
import uuid
from typing import Any, List
from uuid import UUID

from azure.cli.core import get_default_cli
from onefuzz.api import Onefuzz
from onefuzztypes.models import ApiAccessRule


def az_cli(args: List[str]) -> Any:
    cli = get_default_cli()
    cli.logging_cls
    cli.invoke(args, out_file=open(os.devnull, "w"))
    if cli.result.result:
        return cli.result.result
    elif cli.result.error:
        raise cli.result.error


class APIRestrictionTests:
    def __init__(
        self, resource_group: str = None, onefuzz_config_path: str = None
    ) -> None:
        self.onefuzz = Onefuzz(config_path=onefuzz_config_path)
        self.intial_config = self.onefuzz.instance_config.get()

        self.instance_name = urlparse(self.onefuzz.config().endpoint).netloc.split(".")[
            0
        ]
        if resource_group:
            self.resource_group = resource_group
        else:
            self.resource_group = self.instance_name

    def restore_config(self) -> None:
        self.onefuzz.instance_config.update(self.intial_config)

    def assign(self, group_id: UUID, member_id: UUID) -> None:
        instance_config = self.onefuzz.instance_config.get()
        if instance_config.group_membership is None:
            instance_config.group_membership = {}
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
            ]
        )
        member_id = UUID(onefuzz_service_appId["objectId"])
        self.assign(group_id, member_id)

    def test_restriction_on_current_user(self) -> None:

        print("Checking that the current user can get jobs")
        self.onefuzz.jobs.list()

        print("Creating test group")
        group_id = uuid.uuid4()

        print("Adding restriction to the jobs endpoint")
        instance_config = self.onefuzz.instance_config.get()
        if instance_config.api_access_rules is None:
            instance_config.api_access_rules = {}

        instance_config.api_access_rules["/api/jobs"] = ApiAccessRule(
            allowed_groups=[group_id],
            methods=["GET"],
        )

        self.onefuzz.instance_config.update(instance_config)
        restart_instance(self.instance_name, self.resource_group)
        print("Checking that the current user cannot get jobs")
        try:
            self.onefuzz.jobs.list()
            failed = False
        except Exception:
            failed = True
            pass

        if not failed:
            raise Exception("Current user was able to get jobs")

        print("Assigning current user to test group")
        self.assign_current_user(group_id)
        restart_instance(self.instance_name, self.resource_group)

        print("Checking that the current user can get jobs")
        self.onefuzz.jobs.list()


def restart_instance(instance_name: str, resource_group: str) -> None:
    print("Restarting instance")
    az_cli(
        [
            "functionapp",
            "restart",
            "--name",
            f"{instance_name}",
            "--resource-group",
            f"{resource_group}",
        ]
    )


def disable_api_access_rules_caching(instance_name: str, resource_group: str) -> None:
    print("Disabling API access rules caching")
    az_cli(
        [
            "functionapp",
            "config",
            "appsettings",
            "set",
            "--name",
            f"{instance_name}",
            "--resource-group",
            f"{resource_group}",
            "--settings",
            "NO_REQUEST_ACCESS_RULES_CACHE=''",
        ]
    )

    restart_instance(instance_name, resource_group)


def enable_api_access_rules_caching(instance_name: str, resource_group: str) -> None:
    print("Enabling API access rules caching")
    az_cli(
        [
            "functionapp",
            "config",
            "appsettings",
            "delete",
            "--name",
            f"{instance_name}",
            "--resource-group",
            f"{resource_group}",
            "--setting-names",
            "NO_REQUEST_ACCESS_RULES_CACHE",
        ]
    )

    restart_instance(instance_name, resource_group)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--config_path", default=None)
    parser.add_argument("--resource_group", default=None)
    args = parser.parse_args()
    tester = APIRestrictionTests(args.resource_group, args.config_path)

    try:
        disable_api_access_rules_caching(tester.instance_name, tester.resource_group)
        print("test current user restriction")
        tester.test_restriction_on_current_user()
    finally:
        pass
        enable_api_access_rules_caching(tester.instance_name, tester.resource_group)
        tester.restore_config()


if __name__ == "__main__":
    main()
