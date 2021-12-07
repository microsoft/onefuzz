#!/usr/bin/env python3
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import argparse
import os
import subprocess
import sys
import tempfile
import time
import uuid
from typing import Callable, List, Optional, Tuple, TypeVar

import requests
from github import Github

from cleanup_ad import delete_current_user_app_registrations

A = TypeVar("A")


def wait(func: Callable[[], Tuple[bool, str, A]], frequency: float = 1.0) -> A:
    """
    Wait until the provided func returns True

    Provides user feedback via a spinner if stdout is a TTY.
    """

    isatty = sys.stdout.isatty()
    frames = ["-", "\\", "|", "/"]
    waited = False
    last_message = None
    result = None

    try:
        while True:
            result = func()
            if result[0]:
                break
            message = result[1]

            if isatty:
                if last_message:
                    if last_message == message:
                        sys.stdout.write("\b" * (len(last_message) + 2))
                    else:
                        sys.stdout.write("\n")
                sys.stdout.write("%s %s" % (frames[0], message))
                sys.stdout.flush()
            elif last_message != message:
                print(message, flush=True)

            last_message = message
            waited = True
            time.sleep(frequency)
            frames.sort(key=frames[0].__eq__)
    finally:
        if waited and isatty:
            print(flush=True)

    return result[2]


class Downloader:
    def __init__(self) -> None:
        self.gh = Github(login_or_token=os.environ["GITHUB_ISSUE_TOKEN"])

    def update_pr(self, repo_name: str, pr: int) -> None:
        pr_obj = self.gh.get_repo(repo_name).get_pull(pr)
        if pr_obj.mergeable_state == "behind":
            print(f"pr:{pr} out of date.  Updating")
            pr_obj.update_branch()  # type: ignore
            time.sleep(5)
        elif pr_obj.mergeable_state == "dirty":
            raise Exception(f"merge confict errors on pr:{pr}")

    def merge_pr(self, repo_name: str, pr: int) -> None:
        pr_obj = self.gh.get_repo(repo_name).get_pull(pr)
        if pr_obj.mergeable_state == "clean":
            print(f"merging pr:{pr}")
            pr_obj.merge(commit_message="", merge_method="squash")
        else:
            print(f"unable to merge pr:{pr}", pr_obj.mergeable_state)

    def get_artifact(
        self,
        repo_name: str,
        workflow: str,
        branch: Optional[str],
        pr: Optional[int],
        name: str,
        filename: str,
    ) -> None:
        print(f"getting {name}")

        if pr:
            self.update_pr(repo_name, pr)
            branch = self.gh.get_repo(repo_name).get_pull(pr).head.ref
        if not branch:
            raise Exception("missing branch")

        zip_file_url = self.get_artifact_url(repo_name, workflow, branch, name)

        (code, resp, _) = self.gh._Github__requester.requestBlob(  # type: ignore
            "GET", zip_file_url, {}
        )
        if code != 302:
            raise Exception(f"unexpected response: {resp}")

        with open(filename, "wb") as handle:
            for chunk in requests.get(resp["location"], stream=True).iter_content(
                chunk_size=1024 * 16
            ):
                handle.write(chunk)

    def get_artifact_url(
        self, repo_name: str, workflow_name: str, branch: str, name: str
    ) -> str:
        repo = self.gh.get_repo(repo_name)
        workflow = repo.get_workflow(workflow_name)
        runs = workflow.get_runs()
        run = None
        for x in runs:
            if x.head_branch != branch:
                continue
            run = x
            break
        if not run:
            raise Exception("invalid run")

        print("using run from branch", run.head_branch)

        def check() -> Tuple[bool, str, None]:
            if run is None:
                raise Exception("invalid run")
            run.update()
            return run.status == "completed", run.status, None

        wait(check, frequency=10.0)
        if run.conclusion != "success":
            raise Exception(f"bad conclusion: {run.conclusion}")

        response = requests.get(run.artifacts_url).json()
        for artifact in response["artifacts"]:
            if artifact["name"] == name:
                return str(artifact["archive_download_url"])
        raise Exception(f"no archive url for {branch} - {name}")


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
        self.client_id = ""
        self.client_secret = ""

    def merge(self) -> None:
        if self.pr:
            self.downloader.merge_pr(self.branch, self.pr)

    def deploy(self, filename: str) -> None:
        print(f"deploying {filename} to {self.instance}")
        venv = "deploy-venv"
        subprocess.check_call(f"python3 -mvenv {venv}", shell=True)
        pip = venv_path(venv, "pip")
        py = venv_path(venv, "python3")
        config = os.path.join(os.getcwd(), "config.json")
        commands = [
            ("extracting release-artifacts", f"unzip -qq {filename}"),
            ("extracting deployment", "unzip -qq onefuzz-deployment*.zip"),
            ("installing wheel", f"{pip} install -q wheel"),
            ("installing prereqs", f"{pip} install -q -r requirements.txt"),
            (
                "running deployment",
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
        py = venv_path(venv, "python3")

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
        time.sleep(10)
        return

    def test(self, filename: str) -> None:
        venv = "test-venv"
        subprocess.check_call(f"python3 -mvenv {venv}", shell=True)
        py = venv_path(venv, "python3")
        test_dir = "integration-test-artifacts"
        script = "integration-test.py"
        endpoint = f"https://{self.instance}.azurewebsites.net"
        test_args = " ".join(self.test_args)
        unattended_args = (
            f" --client_id {self.client_id} --client_secret {self.client_secret}"
        )
        if self.unattended:
            test_args.join(unattended_args)
        commands = [
            (
                "extracting integration-test-artifacts",
                f"unzip -qq {filename} -d {test_dir}",
            ),
            ("test venv", f"python3 -mvenv {venv}"),
            ("installing wheel", f"./{venv}/bin/pip install -q wheel"),
            ("installing sdk", f"./{venv}/bin/pip install -q sdk/*.whl"),
            (
                "running integration",
                (
                    f"{py} {test_dir}/{script} test {test_dir} "
                    f"--region {self.region} --endpoint {endpoint} "
                    f"{test_args}"
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
