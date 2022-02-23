import argparse
import os
import sys
import time
from pathlib import Path
from typing import Callable, Optional, Tuple, TypeVar

import requests
from github import Github

A = TypeVar("A")


def wait(func: Callable[[], Tuple[bool, str, A]], frequency: float = 1.0) -> A:
    """
    Wait until the provided func returns True.

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


class GithubClient:
    def __init__(self) -> None:
        self.gh = Github(login_or_token=os.environ["GITHUB_ISSUE_TOKEN"])

    def update_pr(self, repo_name: str, pr: int, update_if_behind: bool = True) -> None:
        pr_obj = self.gh.get_repo(repo_name).get_pull(pr)
        if (pr_obj.mergeable_state == "behind") and update_if_behind:
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
        file_path: str,
        update_branch: bool = True,
    ) -> None:
        print(f"getting {name}")

        if pr:
            self.update_pr(repo_name, pr, update_branch)
            branch = self.gh.get_repo(repo_name).get_pull(pr).head.ref
        if not branch:
            raise Exception("missing branch")

        zip_file_url = self.get_artifact_url(repo_name, workflow, branch, name)

        (code, resp, _) = self.gh._Github__requester.requestBlob(  # type: ignore
            "GET", zip_file_url, {}
        )
        if code != 302:
            raise Exception(f"unexpected response: {resp}")

        with open(file_path, "wb") as handle:
            print(f"writing {file_path}")
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


def download_artifacts(
    downloader: GithubClient,
    repo: str,
    branch: Optional[str],
    pr: Optional[int],
    directory: str,
    update_branch: bool = True,
) -> None:
    release_filename = "release-artifacts.zip"

    downloader.get_artifact(
        repo,
        "ci.yml",
        branch,
        pr,
        "release-artifacts",
        os.path.join(directory, release_filename),
        update_branch,
    )

    test_filename = "integration-test-artifacts.zip"
    downloader.get_artifact(
        repo,
        "ci.yml",
        branch,
        pr,
        "integration-test-artifacts",
        os.path.join(directory, test_filename),
        update_branch,
    )


def main() -> None:

    parser = argparse.ArgumentParser()
    group = parser.add_mutually_exclusive_group()
    group.add_argument("--branch")
    group.add_argument("--pr", type=int)
    parser.add_argument("--repo", default="microsoft/onefuzz")
    parser.add_argument("--destination", default=os.getcwd())
    parser.add_argument("--skip_update", action="store_true")

    args = parser.parse_args()
    path = Path(args.destination)
    path.mkdir(parents=True, exist_ok=True)

    downloader = GithubClient()
    download_artifacts(
        downloader,
        args.repo,
        args.branch,
        args.pr,
        args.destination,
        not args.skip_update,
    )


if __name__ == "__main__":
    main()
