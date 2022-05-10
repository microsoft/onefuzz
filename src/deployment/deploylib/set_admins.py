#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import argparse
from uuid import UUID

from azure.cosmosdb.table.tableservice import TableService
from azure.identity import AzureCliCredential
from azure.mgmt.resource import SubscriptionClient
from azure.mgmt.storage import StorageManagementClient

from deploylib.configuration import (
    InstanceConfigClient,
    update_admins,
    update_allowed_aad_tenants,
)


def main() -> None:
    formatter = argparse.ArgumentDefaultsHelpFormatter
    parser = argparse.ArgumentParser(formatter_class=formatter)
    parser.add_argument("resource_group")
    parser.add_argument("storage_account")
    parser.add_argument("--admins", type=UUID, nargs="*")
    parser.add_argument("--allowed_aad_tenants", type=UUID, nargs="*")
    args = parser.parse_args()

    credential = AzureCliCredential()
    client = StorageManagementClient(credential)
    storage_keys = client.storage_accounts.list_keys(
        args.resource_group, args.storage_account
    )
    table_service = TableService(
        account_name=args.storage_account, account_key=storage_keys.keys[0].value
    )
    config_client = InstanceConfigClient(table_service, args.resource_group)
    if args.admins:
        update_admins(config_client, args.admins)
    if args.allowed_aad_tenants:
        update_allowed_aad_tenants(config_client, args.allowed_aad_tenants)


if __name__ == "__main__":
    main()
