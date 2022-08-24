#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from typing import Any, Iterator, List, Optional, Tuple, Union

from azure.devops.connection import Connection
from azure.devops.credentials import BasicAuthentication
from azure.devops.exceptions import (
    AzureDevOpsAuthenticationError,
    AzureDevOpsClientError,
    AzureDevOpsClientRequestError,
    AzureDevOpsServiceError,
)
from azure.devops.v6_0.work_item_tracking.models import (
    CommentCreate,
    JsonPatchOperation,
    Wiql,
    WorkItem,
)
from azure.devops.v6_0.work_item_tracking.work_item_tracking_client import (
    WorkItemTrackingClient,
)
from memoization import cached
from onefuzztypes.models import ADOTemplate, RegressionReport, Report
from onefuzztypes.primitives import Container

from ..secrets import get_secret_string_value
from .common import Render, fail_task


class AdoNotificationException(Exception):
    pass


@cached(ttl=60)
def get_ado_client(base_url: str, token: str) -> WorkItemTrackingClient:
    connection = Connection(base_url=base_url, creds=BasicAuthentication("PAT", token))
    client = connection.clients_v6_0.get_work_item_tracking_client()
    return client


@cached(ttl=60)
def get_valid_fields(
    client: WorkItemTrackingClient, project: Optional[str] = None
) -> List[str]:
    valid_fields = [
        x.reference_name.lower()
        for x in client.get_fields(project=project, expand="ExtensionFields")
    ]
    return valid_fields


class ADO:
    def __init__(
        self,
        container: Container,
        filename: str,
        config: ADOTemplate,
        report: Report,
        *,
        renderer: Optional[Render] = None,
    ):
        self.config = config
        if renderer:
            self.renderer = renderer
        else:
            self.renderer = Render(container, filename, report)
        self.project = self.render(self.config.project)

    def connect(self) -> None:
        auth_token = get_secret_string_value(self.config.auth_token)
        self.client = get_ado_client(self.config.base_url, auth_token)

    def render(self, template: str) -> str:
        return self.renderer.render(template)

    def existing_work_items(self) -> Iterator[WorkItem]:
        filters = {}
        for key in self.config.unique_fields:
            if key == "System.TeamProject":
                value = self.render(self.config.project)
            else:
                value = self.render(self.config.ado_fields[key])
            filters[key.lower()] = value

        valid_fields = get_valid_fields(
            self.client, project=filters.get("system.teamproject")
        )

        post_query_filter = {}

        # WIQL (Work Item Query Language) is an SQL like query language that
        # doesn't support query params, safe quoting, or any other SQL-injection
        # protection mechanisms.
        #
        # As such, build the WIQL with a those fields we can pre-determine are
        # "safe" and otherwise use post-query filtering.
        parts = []
        for k, v in filters.items():
            # Only add pre-system approved fields to the query
            if k not in valid_fields:
                post_query_filter[k] = v
                continue

            # WIQL supports wrapping values in ' or " and escaping ' by doubling it
            #
            # For this System.Title: hi'there
            # use this query fragment: [System.Title] = 'hi''there'
            #
            # For this System.Title: hi"there
            # use this query fragment: [System.Title] = 'hi"there'
            #
            # For this System.Title: hi'"there
            # use this query fragment: [System.Title] = 'hi''"there'
            SINGLE = "'"
            parts.append("[%s] = '%s'" % (k, v.replace(SINGLE, SINGLE + SINGLE)))

        query = "select [System.Id] from WorkItems"
        if parts:
            query += " where " + " AND ".join(parts)

        wiql = Wiql(query=query)
        for entry in self.client.query_by_wiql(wiql).work_items:
            item = self.client.get_work_item(entry.id, expand="Fields")
            lowered_fields = {x.lower(): str(y) for (x, y) in item.fields.items()}
            if post_query_filter and not all(
                [
                    k.lower() in lowered_fields and lowered_fields[k.lower()] == v
                    for (k, v) in post_query_filter.items()
                ]
            ):
                continue
            yield item

    def update_existing(self, item: WorkItem) -> None:
        if self.config.on_duplicate.comment:
            comment = self.render(self.config.on_duplicate.comment)
            self.client.add_comment(
                CommentCreate(comment),
                self.project,
                item.id,
            )

        document = []
        for field in self.config.on_duplicate.increment:
            value = int(item.fields[field]) if field in item.fields else 0
            value += 1
            document.append(
                JsonPatchOperation(
                    op="Replace", path="/fields/%s" % field, value=str(value)
                )
            )

        for field in self.config.on_duplicate.ado_fields:
            field_value = self.render(self.config.on_duplicate.ado_fields[field])
            document.append(
                JsonPatchOperation(
                    op="Replace", path="/fields/%s" % field, value=field_value
                )
            )

        if item.fields["System.State"] in self.config.on_duplicate.set_state:
            document.append(
                JsonPatchOperation(
                    op="Replace",
                    path="/fields/System.State",
                    value=self.config.on_duplicate.set_state[
                        item.fields["System.State"]
                    ],
                )
            )

        if document:
            self.client.update_work_item(document, item.id, project=self.project)

    def render_new(self) -> Tuple[str, List[JsonPatchOperation]]:
        task_type = self.render(self.config.type)
        document = []
        if "System.Tags" not in self.config.ado_fields:
            document.append(
                JsonPatchOperation(
                    op="Add", path="/fields/System.Tags", value="Onefuzz"
                )
            )

        for field in self.config.ado_fields:
            value = self.render(self.config.ado_fields[field])
            if field == "System.Tags":
                value += ";Onefuzz"
            document.append(
                JsonPatchOperation(op="Add", path="/fields/%s" % field, value=value)
            )
        return (task_type, document)

    def create_new(self) -> Any:
        task_type, document = self.render_new()

        entry = self.client.create_work_item(
            document=document, project=self.project, type=task_type
        )

        if self.config.comment:
            comment = self.render(self.config.comment)
            self.client.add_comment(
                CommentCreate(comment),
                self.project,
                entry.id,
            )
        return entry

    def process(self, notification_info: str) -> None:
        seen = False
        for work_item in self.existing_work_items():
            self.update_existing(work_item)
            logging.info(
                f"notify ado: updated work item {work_item.id} - {notification_info}"
            )
            seen = True

        if not seen:
            self.create_new()
            logging.info(
                f"notify ado: created new work item {work_item.id} - {notification_info}"
            )


def is_transient(err: Exception) -> bool:
    error_codes = [
        # "TF401349: An unexpected error has occurred, please verify your request and try again." # noqa: E501
        "TF401349",
        # TF26071: This work item has been changed by someone else since you opened it. You will need to refresh it and discard your changes. # noqa: E501
        "TF26071",
    ]
    error_str = str(err)
    for code in error_codes:
        if code in error_str:
            return True
    return False


def notify_ado(
    config: ADOTemplate,
    container: Container,
    filename: str,
    report: Union[Report, RegressionReport],
    fail_task_on_transient_error: bool,
) -> None:
    if isinstance(report, RegressionReport):
        logging.info(
            "ado integration does not support regression reports. "
            "container:%s filename:%s",
            container,
            filename,
        )
        return

    notification_info = f"job_id:%s{report.job_id} task_id:{report.task_id} container:{container} filename:{filename}"

    logging.info("notify ado: %s", notification_info)

    try:
        ado = ADO(container, filename, config, report)
        ado.connect()
        ado.process(notification_info)
    except (
        AzureDevOpsAuthenticationError,
        AzureDevOpsClientError,
        AzureDevOpsServiceError,
        AzureDevOpsClientRequestError,
        ValueError,
    ) as err:

        if not fail_task_on_transient_error and is_transient(err):
            raise AdoNotificationException(
                f"transient ADO notification failure {notification_info}"
            ) from err
        else:
            fail_task(report, err)
