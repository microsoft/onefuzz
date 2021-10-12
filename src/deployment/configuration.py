#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
import logging
from typing import List, Optional
from uuid import UUID

from azure.cosmosdb.table.tableservice import TableService

storage_client_logger = logging.getLogger("azure.cosmosdb.table.common.storageclient")
TABLE_NAME = "InstanceConfig"

logger = logging.getLogger("deploy")


## Disable logging from storageclient. This module displays an error message
## when a resource is not found even if the exception is raised and handled internally.
## This happen when a table does not exist. An error message is displayed but the exception is
## handled by the library.
def disable_storage_client_logging() -> None:
    if storage_client_logger:
        storage_client_logger.disabled = True


def enable_storage_client_logging() -> None:
    if storage_client_logger:
        storage_client_logger.disabled = False


def create_if_missing(table_service: TableService) -> None:
    try:
        disable_storage_client_logging()

        if not table_service.exists(TABLE_NAME):
            table_service.create_table(TABLE_NAME)
    finally:
        enable_storage_client_logging()


def update_allowed_aad_tenants(
    table_service: TableService, resource_group: str, tenants: List[UUID]
) -> None:
    create_if_missing(table_service)
    as_str = [str(x) for x in tenants]
    table_service.insert_or_merge_entity(
        TABLE_NAME,
        {
            "PartitionKey": resource_group,
            "RowKey": resource_group,
            "allowed_aad_tenants": json.dumps(as_str),
        },
    )


def update_admins(
    table_service: TableService, resource_group: str, admins: List[UUID]
) -> None:
    create_if_missing(table_service)
    admins_as_str: Optional[List[str]] = None
    if admins:
        admins_as_str = [str(x) for x in admins]

    table_service.insert_or_merge_entity(
        TABLE_NAME,
        {
            "PartitionKey": resource_group,
            "RowKey": resource_group,
            "admins": json.dumps(admins_as_str),
        },
    )


def update_nsg(
    table_service: TableService,
    resource_group: str,
    allowed_rules: List[str],
) -> None:
    create_if_missing(table_service)
    
    rules_as_str = allowed_rules.split(" ")

    nsg_config = {
        "allowed_ips": rules_as_str
    }
    table_service.insert_or_merge_entity(
        TABLE_NAME,
        {
            "PartitionKey": resource_group,
            "RowKey": resource_group,
            "proxy_nsg_config": json.dumps(nsg_config),
        },
    )
    # if nsg_tag_rules:
    #     table_service.insert_or_merge_entity(
    #         TABLE_NAME,
    #         {
    #             "PartitionKey": resource_group,
    #             "RowKey": resource_group,
    #             "admins": json.dumps(nsg_ip_rules),
    #         },
    #     )


if __name__ == "__main__":
    pass
