#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import argparse
import os
import subprocess
import tempfile
import time
import uuid
from typing import List, Optional

from cleanup_ad import delete_current_user_app_registrations

from .download_artifacts import Downloader


def venv_path(base: str, name: str) -> str:
    for subdir in ["bin", "Scripts"]:
        path = os.path.join(base, subdir, name)
        for ext in ["", ".exe"]:
            path += ext
            if os.path.exists(path):
                return path
    raise Exception("missing venv")


class Deployer:
    def __init__(
        self,
        *,
        pr: int,
        branch: str,
        instance: str,
        region: str,
        subscription_id: Optional[str],
        authority: Optional[str],
        skip_tests: bool,
        test_args: List[str],
        repo: str,
        unattended: bool,
    ):
        self.downloader = Downloader()
        self.pr = pr
        self.branch = branch
        self.instance = instance
        self.region = region
        self.subscription_id = subscription_id
        self.skip_tests = skip_tests
        self.test_args = test_args or []
        self.repo = repo
        self.unattended = unattended
        self.client_id: Optional[str] = None
        self.client_secret: Optional[str] = None
        self.authority = authority

    def merge(self) -> None:
        if self.pr:
            self.downloader.merge_pr(self.branch, self.pr)

    def deploy(self, filename: str) -> None:
        print(f"deploying {filename} to {self.instance}")
        venv = "deploy-venv"
        subprocess.check_call(f"python -mvenv {venv}", shell=True)
        pip = venv_path(venv, "pip")
        py = venv_path(venv, "python")
        config = os.path.join(os.getcwd(), "config.json")
        commands = [
            ("extracting release-artifacts", f"unzip -qq {filename}"),
            ("extracting deployment", "unzip -qq onefuzz-deployment*.zip"),
            ("installing wheel", f"{pip} install -q wheel"),
            ("installing prereqs", f"{pip} install -q -r requirements.txt"),
            (
                "running deploment",
                (
                    f"{py} deploy.py {self.region} "
                    f"{self.instance} {self.instance} cicd {config}"
                    f" {' --subscription_id ' + self.subscription_id if self.subscription_id else ''}"
                ),
            ),
        ]
        for (msg, cmd) in commands:
            print(msg)
            subprocess.check_call(cmd, shell=True)

        if self.unattended:
            self.register()

    def register(self) -> None:
        sp_name = "sp_" + self.instance
        print(f"registering {sp_name} to {self.instance}")

        venv = "deploy-venv"
        pip = venv_path(venv, "pip")
        py = venv_path(venv, "python")

        az_cmd = ["az", "account", "show", "--query", "id", "-o", "tsv"]
        subscription_id = subprocess.check_output(az_cmd, encoding="UTF-8")
        subscription_id = subscription_id.strip()

        commands = [
            ("installing prereqs", f"{pip} install -q -r requirements.txt"),
            (
                "running cli registration",
                (
                    f"{py} ./deploylib/registration.py create_cli_registration "
                    f"{self.instance} {subscription_id}"
                    f" --registration_name {sp_name}"
                ),
            ),
        ]

        for (msg, cmd) in commands:
            print(msg)
            output = subprocess.check_output(cmd, shell=True, encoding="UTF-8")
            if "client_id" in output:
                output_list = output.split("\n")
                for line in output_list:
                    if "client_id" in line:
                        line_list = line.split(":")
                        client_id = line_list[1].strip()
                        self.client_id = client_id
                        print(("client_id: " + client_id))
                    if "client_secret" in line:
                        line_list = line.split(":")
                        client_secret = line_list[1].strip()
                        self.client_secret = client_secret
        time.sleep(30)
        return

    def test(self, filename: str) -> None:
        venv = "test-venv"
        subprocess.check_call(f"python -mvenv {venv}", shell=True)
        py = venv_path(venv, "python")
        test_dir = "integration-test-artifacts"
        script = "integration-test.py"
        endpoint = f"https://{self.instance}.azurewebsites.net"
        test_args = " ".join(self.test_args)
        unattended_args = (
            f"--client_id {self.client_id} --client_secret {self.client_secret}"
            if self.unattended
            else ""
        )
        authority_args = f"--authority {self.authority}" if self.authority else ""

        commands = [
            (
                "extracting integration-test-artifacts",
                f"unzip -qq {filename} -d {test_dir}",
            ),
            ("test venv", f"python -mvenv {venv}"),
            ("installing wheel", f"./{venv}/bin/pip install -q wheel"),
            ("installing sdk", f"./{venv}/bin/pip install -q sdk/*.whl"),
            (
                "running integration",
                (
                    f"{py} {test_dir}/{script} test {test_dir} "
                    f"--region {self.region} --endpoint {endpoint} "
                    f"{authority_args} "
                    f"{unattended_args} {test_args}"
                ),
            ),
        ]
        for (msg, cmd) in commands:
            print(msg)
            print(cmd)
            subprocess.check_call(cmd, shell=True)

    def cleanup(self, skip: bool) -> None:
        os.chdir(tempfile.gettempdir())
        if skip:
            return

        cmd = ["az", "group", "delete", "-n", self.instance, "--yes", "--no-wait"]
        print(cmd)
        subprocess.call(cmd)

        delete_current_user_app_registrations(self.instance)
        print("done")

    def run(self, *, merge_on_success: bool = False) -> None:
        release_filename = "release-artifacts.zip"
        self.downloader.get_artifact(
            self.repo,
            "ci.yml",
            self.branch,
            self.pr,
            "release-artifacts",
            release_filename,
        )

        test_filename = "integration-test-artifacts.zip"
        self.downloader.get_artifact(
            self.repo,
            "ci.yml",
            self.branch,
            self.pr,
            "integration-test-artifacts",
            test_filename,
        )

        self.deploy(release_filename)

        if not self.skip_tests:
            self.test(test_filename)

        if merge_on_success:
            self.merge()


def main() -> None:
    # Get a name that can be added to the resource group name
    # to make it easy to identify the owner
    cmd = ["az", "ad", "signed-in-user", "show", "--query", "mailNickname", "-o", "tsv"]
    name = subprocess.check_output(cmd, encoding="UTF-8")

    # The result from az includes a newline
    # which we strip out.
    name = name.strip()

    default_instance = f"pr-check-{name}-%s" % uuid.uuid4().hex
    parser = argparse.ArgumentParser()
    parser.add_argument("--instance", default=default_instance)
    group = parser.add_mutually_exclusive_group()
    group.add_argument("--branch")
    group.add_argument("--pr", type=int)

    parser.add_argument("--repo", default="microsoft/onefuzz")
    parser.add_argument("--region", default="eastus2")
    parser.add_argument("--skip-tests", action="store_true")
    parser.add_argument("--skip-cleanup", action="store_true")
    parser.add_argument("--skip-cleanup-on-failure", action="store_true")
    parser.add_argument("--merge-on-success", action="store_true")
    parser.add_argument("--subscription_id")
    parser.add_argument("--authority", default=None)
    parser.add_argument("--test_args", nargs=argparse.REMAINDER)
    parser.add_argument("--unattended", action="store_true")
    args = parser.parse_args()

    if not args.branch and not args.pr:
        raise Exception("--branch or --pr is required")

    d = Deployer(
        branch=args.branch,
        pr=args.pr,
        instance=args.instance,
        region=args.region,
        subscription_id=args.subscription_id,
        skip_tests=args.skip_tests,
        test_args=args.test_args,
        repo=args.repo,
        unattended=args.unattended,
        authority=args.authority,
    )
    with tempfile.TemporaryDirectory() as directory:
        os.chdir(directory)
        print(f"running from within {directory}")

        try:
            d.run(merge_on_success=args.merge_on_success)
            d.cleanup(args.skip_cleanup)
            return
        finally:
            if not args.skip_cleanup_on_failure:
                d.cleanup(args.skip_cleanup)
        os.chdir(tempfile.gettempdir())


if __name__ == "__main__":
    main()
