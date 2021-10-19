#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import ipaddress
import json
import logging
from typing import List, Optional
from uuid import UUID

from azure.cosmosdb.table.tableservice import TableService

storage_client_logger = logging.getLogger("azure.cosmosdb.table.common.storageclient")
TABLE_NAME = "InstanceConfig"

logger = logging.getLogger("deploy")


class InstanceConfigClient:

    table_service: TableService
    resource_group: str

    def __init__(self, table_service: TableService, resource_group: str):
        self.resource_group = resource_group
        self.table_service = table_service
        self.create_if_missing(table_service)

    def disable_storage_client_logging(self) -> None:
        if storage_client_logger:
            storage_client_logger.disabled = True

    def enable_storage_client_logging(self) -> None:
        if storage_client_logger:
            storage_client_logger.disabled = False

    def create_if_missing(self, table_service: TableService) -> None:
        try:
            self.disable_storage_client_logging()

            if not table_service.exists(TABLE_NAME):
                table_service.create_table(TABLE_NAME)
        finally:
            self.enable_storage_client_logging()


class NsgRule:

    rule: str
    is_tag: bool

    def __init__(self, rule: str):
        try:
            self.is_tag = False
            self.check_rule(rule)
            self.rule = rule
        except Exception:
            raise ValueError(
                "Invalid rule. Please provide a valid rule or supply the wild card *."
            )

    def check_rule(self, value: str) -> None:
        if value is None and len(value.strip()) == 0:
            raise ValueError(
                "Rule can not be None or empty string. Please provide a valid rule or supply the wild card *."
            )

        # Check if IP Address
        try:
            ipaddress.ip_address(value)
        except ValueError:
            pass
        # Check if IP Range
        try:
            ipaddress.ip_network(value)
        except ValueError:
            pass

        self.is_tag = True


## Disable logging from storageclient. This module displays an error message
## when a resource is not found even if the exception is raised and handled internally.
## This happen when a table does not exist. An error message is displayed but the exception is
## handled by the library.
# def disable_storage_client_logging() -> None:
#     if storage_client_logger:
#         storage_client_logger.disabled = True


# def enable_storage_client_logging() -> None:
#     if storage_client_logger:
#         storage_client_logger.disabled = False


# def create_if_missing(table_service: TableService) -> None:
#     try:
#         disable_storage_client_logging()

#         if not table_service.exists(TABLE_NAME):
#             table_service.create_table(TABLE_NAME)
#     finally:
#         enable_storage_client_logging()


def update_allowed_aad_tenants(
    config_client: InstanceConfigClient, tenants: List[UUID]
) -> None:
    as_str = [str(x) for x in tenants]
    config_client.table_service.insert_or_merge_entity(
        TABLE_NAME,
        {
            "PartitionKey": config_client.resource_group,
            "RowKey": config_client.resource_group,
            "allowed_aad_tenants": json.dumps(as_str),
        },
    )


def update_admins(config_client: InstanceConfigClient, admins: List[UUID]) -> None:
    admins_as_str: Optional[List[str]] = None
    if admins:
        admins_as_str = [str(x) for x in admins]

    config_client.table_service.insert_or_merge_entity(
        TABLE_NAME,
        {
            "PartitionKey": config_client.resource_group,
            "RowKey": config_client.resource_group,
            "admins": json.dumps(admins_as_str),
        },
    )


def parse_rules(rules_str: str) -> List[NsgRule]:
    rules_list = rules_str.split(",")

    nsg_rules = []
    for rule in rules_list:
        try:
            nsg_rule = NsgRule(rule)
            nsg_rules.append(nsg_rule)
        except Exception:
            raise ValueError(
                "One or more input rules was invalid. Please enter a comma-separted list if valid sources."
            )
    return nsg_rules


def update_nsg(
    config_client: InstanceConfigClient,
    allowed_rules: List[NsgRule],
) -> None:
    # create class initialized by table service/resource group outside function that's checked in deploy.py
    config_client.table_service.insert_or_merge_entity(
        TABLE_NAME,
        {
            "PartitionKey": config_client.resource_group,
            "RowKey": config_client.resource_group,
            "proxy_nsg_config": json.dumps(allowed_rules),
        },
    )


if __name__ == "__main__":
    pass
