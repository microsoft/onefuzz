#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from typing import List, Optional, Union

from github3 import login
from github3.exceptions import GitHubException
from github3.issues import Issue
from onefuzztypes.enums import GithubIssueSearchMatch
from onefuzztypes.models import (
    GithubAuth,
    GithubIssueTemplate,
    RegressionReport,
    Report,
)
from onefuzztypes.primitives import Container

from ..secrets import get_secret_obj
from .common import Render, fail_task


class GithubNotificationException(Exception):
    pass


class GithubIssue:
    def __init__(
        self,
        config: GithubIssueTemplate,
        container: Container,
        filename: str,
        report: Report,
    ):
        self.config = config
        self.report = report
        if isinstance(config.auth.secret, GithubAuth):
            auth = config.auth.secret
        else:
            auth = get_secret_obj(config.auth.secret.url, GithubAuth)

        self.gh = login(username=auth.user, password=auth.personal_access_token)
        self.renderer = Render(container, filename, report)

    def render(self, field: str) -> str:
        return self.renderer.render(field)

    def existing(self) -> List[Issue]:
        query = [
            self.render(self.config.unique_search.string),
            "repo:%s/%s"
            % (
                self.render(self.config.organization),
                self.render(self.config.repository),
            ),
        ]
        if self.config.unique_search.author:
            query.append("author:%s" % self.render(self.config.unique_search.author))

        if self.config.unique_search.state:
            query.append("state:%s" % self.config.unique_search.state.name)

        issues = []
        title = self.render(self.config.title)
        body = self.render(self.config.body)
        for issue in self.gh.search_issues(" ".join(query)):
            skip = False
            for field in self.config.unique_search.field_match:
                if field == GithubIssueSearchMatch.title and issue.title != title:
                    skip = True
                    break
                if field == GithubIssueSearchMatch.body and issue.body != body:
                    skip = True
                    break
            if not skip:
                issues.append(issue)

        return issues

    def update(self, issue: Issue) -> None:
        logging.info("updating issue: %s", issue)
        if self.config.on_duplicate.comment:
            issue.issue.create_comment(self.render(self.config.on_duplicate.comment))
        if self.config.on_duplicate.labels:
            labels = [self.render(x) for x in self.config.on_duplicate.labels]
            issue.issue.edit(labels=labels)
        if self.config.on_duplicate.reopen and issue.state != "open":
            issue.issue.edit(state="open")

    def create(self) -> None:
        logging.info("creating issue")

        assignees = [self.render(x) for x in self.config.assignees]
        labels = list(set(["OneFuzz"] + [self.render(x) for x in self.config.labels]))

        self.gh.create_issue(
            self.render(self.config.organization),
            self.render(self.config.repository),
            self.render(self.config.title),
            body=self.render(self.config.body),
            labels=labels,
            assignees=assignees,
        )

    def process(self) -> None:
        issues = self.existing()
        if issues:
            self.update(issues[0])
        else:
            self.create()


def github_issue(
    config: GithubIssueTemplate,
    container: Container,
    filename: str,
    report: Optional[Union[Report, RegressionReport]],
    fail_task_on_error: bool,
) -> None:
    if report is None:
        return
    if isinstance(report, RegressionReport):
        logging.info(
            "github issue integration does not support regression reports. "
            "container:%s filename:%s",
            container,
            filename,
        )
        return

    try:
        handler = GithubIssue(config, container, filename, report)
        handler.process()
    except (GitHubException, ValueError) as err:
        if fail_task_on_error:
            fail_task(report, err)
        else:
            raise GithubNotificationException("Github notification failed") from err
