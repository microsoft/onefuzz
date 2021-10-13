#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import argparse
from uuid import UUID

from azure.common.client_factory import get_client_from_cli_profile
from azure.cosmosdb.table.tableservice import TableService
from azure.mgmt.storage import StorageManagementClient
from configuration import update_admins, update_allowed_aad_tenants


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
