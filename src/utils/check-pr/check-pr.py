#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import argparse
import os
import subprocess
import tempfile
import uuid

from .deploy import Deployer, Tester, TestConfig
from .github_client import GithubClient, download_artifacts


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

    with tempfile.TemporaryDirectory() as directory:
        test_config = TestConfig(
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
            directory=directory,
        )
        githubClient = GithubClient()
        deployer = Deployer(test_config)
        tester = Tester(test_config)

        try:
            download_artifacts(githubClient, args.repo, args.branch, args.pr, directory)
            deployer.deploy()
            tester.run(
                githubClient=githubClient, merge_on_success=args.merge_on_success
            )
            tester.cleanup(args.skip_cleanup)
            return
        finally:
            if not args.skip_cleanup_on_failure:
                tester.cleanup(args.skip_cleanup)
        os.chdir(tempfile.gettempdir())


if __name__ == "__main__":
    main()
