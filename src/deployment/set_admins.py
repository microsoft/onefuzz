#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import argparse
import json
import logging
from typing import List, Optional
from uuid import UUID

from azure.common.client_factory import get_client_from_cli_profile
from azure.cosmosdb.table.tableservice import TableService
from azure.mgmt.storage import StorageManagementClient

## Disable logging from storageclient. This module displays an error message
## when a resource is not found even if the exception is raised and handled internally.
## This happen when a table does not exist. An error message is displayed but the exception is
## handled by the library.
logging.getLogger('azure.cosmosdb.table.common.storageclient').disabled = True

TABLE_NAME = "InstanceConfig"


def create_if_missing(table_service: TableService) -> None:
    if not table_service.exists(TABLE_NAME):
        table_service.create_table(TABLE_NAME)


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


def main() -> None:
    formatter = argparse.ArgumentDefaultsHelpFormatter
    parser = argparse.ArgumentParser(formatter_class=formatter)
    parser.add_argument("resource_group")
    parser.add_argument("storage_account")
    parser.add_argument("--admins", type=UUID, nargs="*")
    parser.add_argument("--allowed_aad_tenants", type=UUID, nargs="*")
    args = parser.parse_args()

    client = get_client_from_cli_profile(StorageManagementClient)
    storage_keys = client.storage_accounts.list_keys(
        args.resource_group, args.storage_account
    )
    table_service = TableService(
        account_name=args.storage_account, account_key=storage_keys.keys[0].value
    )
    if args.admins:
        update_admins(table_service, args.resource_group, args.admins)
    if args.allowed_aad_tenants:
        update_allowed_aad_tenants(
            table_service, args.resource_group, args.allowed_aad_tenants
        )


if __name__ == "__main__":
    main()
