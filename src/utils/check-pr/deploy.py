#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import os
import subprocess
import tempfile
import time
from typing import List, Optional

from cleanup_ad import delete_current_user_app_registrations

from .githubClient import GithubClient


def venv_path(base: str, name: str) -> str:
    for subdir in ["bin", "Scripts"]:
        path = os.path.join(base, subdir, name)
        for ext in ["", ".exe"]:
            path += ext
            if os.path.exists(path):
                return path
    raise Exception("missing venv")


class TestConfig:
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
        directory: str,
    ):
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
        self.release_filename = "release-artifacts.zip"
        self.test_filename = "integration-test-artifacts.zip"
        self.directory = directory


class Deployer:
    def __init__(
        self,
        test_config: TestConfig,
    ):
        self.githubClient = GithubClient()
        self.test_config = test_config

    def deploy(self) -> None:
        os.chdir(self.test_config.directory)
        print(f"running from within {self.test_config.directory}")

        filename = self.test_config.release_filename
        print(f"deploying {filename} to {self.test_config.instance}")
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
                    f"{py} deploy.py {self.test_config.region} "
                    f"{self.test_config.instance} {self.test_config.instance} cicd {config}"
                    f" {' --subscription_id ' + self.test_config.subscription_id if self.test_config.subscription_id else ''}"
                ),
            ),
        ]
        for (msg, cmd) in commands:
            print(msg)
            subprocess.check_call(cmd, shell=True)

        if self.test_config.unattended:
            self.register()

    def register(self) -> None:
        sp_name = "sp_" + self.test_config.instance
        print(f"registering {sp_name} to {self.test_config.instance}")

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
                    f"{self.test_config.instance} {subscription_id}"
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
                        self.test_config.client_id = client_id
                        print(("client_id: " + client_id))
                    if "client_secret" in line:
                        line_list = line.split(":")
                        client_secret = line_list[1].strip()
                        self.test_config.client_secret = client_secret
        time.sleep(30)
        return


class Tester:
    def __init__(self, test_config: TestConfig):
        self.test_config = test_config

    def test(self) -> None:
        venv = "test-venv"
        subprocess.check_call(f"python -mvenv {venv}", shell=True)
        py = venv_path(venv, "python")
        test_dir = "integration-test-artifacts"
        script = "integration-test.py"
        endpoint = f"https://{self.test_config.instance}.azurewebsites.net"
        test_args = " ".join(self.test_config.test_args)
        client_id_arg = (
            f"--client_id {self.test_config.client_id}"
            if self.test_config.client_id
            else ""
        )

        client_secret_arg = (
            f"--client_secret {self.test_config.client_secret}"
            if self.test_config.client_secret
            else ""
        )
        authority_args = (
            f"--authority {self.test_config.authority}"
            if self.test_config.authority
            else ""
        )

        commands = [
            (
                "extracting integration-test-artifacts",
                f"unzip -qq {self.test_config.test_filename} -d {test_dir}",
            ),
            ("test venv", f"python -mvenv {venv}"),
            ("installing wheel", f"./{venv}/bin/pip install -q wheel"),
            ("installing sdk", f"./{venv}/bin/pip install -q sdk/*.whl"),
            (
                "running integration",
                (
                    f"{py} {test_dir}/{script} test {test_dir} "
                    f"--region {self.test_config.region} --endpoint {endpoint} "
                    f"{authority_args} "
                    f"{client_id_arg} {client_secret_arg} {test_args}"
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

        cmd = [
            "az",
            "group",
            "delete",
            "-n",
            self.test_config.instance,
            "--yes",
            "--no-wait",
        ]
        print(cmd)
        subprocess.call(cmd)

        delete_current_user_app_registrations(self.test_config.instance)
        print("done")

    def run(
        self,
        *,
        githubClient: GithubClient,
        skip_tests: bool = False,
        merge_on_success: bool = False,
    ) -> None:
        if not skip_tests:
            self.test()

        if merge_on_success:
            if self.test_config.pr:
                githubClient.merge_pr(self.test_config.branch, self.test_config.pr)
