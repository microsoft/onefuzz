#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import argparse
import json
from typing import Callable, Dict, List
from uuid import UUID

from azure.common.client_factory import get_client_from_cli_profile
from azure.cosmosdb.table.tablebatch import TableBatch
from azure.cosmosdb.table.tableservice import TableService
from azure.mgmt.storage import StorageManagementClient


def migrate_task_os(table_service: TableService) -> None:
    table_name = "Task"
    tasks = table_service.query_entities(
        table_name, select="PartitionKey,RowKey,os,config"
    )
    partitionKey = None

    count = 0
    batch = TableBatch()
    for task in tasks:
        if partitionKey != task.PartitionKey:
            table_service.commit_batch(table_name, batch)
            batch = TableBatch()

        partitionKey = task.PartitionKey
        if "os" not in task or (not task.os):
            config = json.loads(task.config)
            print(config)
            if "windows".lower() in config["vm"]["image"].lower():
                task["os"] = "windows"
            else:
                task["os"] = "linux"
            count = count + 1
        batch.merge_entity(task)
    table_service.commit_batch(table_name, batch)
    print("migrated %s rows" % count)


def migrate_notification_keys(table_service: TableService) -> None:
    table_name = "Notification"
    notifications = table_service.query_entities(
        table_name, select="PartitionKey,RowKey,config"
    )

    count = 0
    for entry in notifications:
        try:
            UUID(entry.PartitionKey)
            continue
        except ValueError:
            pass

        table_service.insert_or_replace_entity(
            table_name,
            {
                "PartitionKey": entry.RowKey,
                "RowKey": entry.PartitionKey,
                "config": entry.config,
            },
        )
        table_service.delete_entity(table_name, entry.PartitionKey, entry.RowKey)
        count += 1

    print("migrated %s rows" % count)


migrations: Dict[str, Callable[[TableService], None]] = {
    "migrate_task_os": migrate_task_os,
    "migrate_notification_keys": migrate_notification_keys,
}


def migrate(table_service: TableService, migration_names: List[str]) -> None:
    for name in migration_names:
        print("applying migration '%s'" % name)
        migrations[name](table_service)
        print("migration '%s' applied" % name)


def main() -> None:
    formatter = argparse.ArgumentDefaultsHelpFormatter
    parser = argparse.ArgumentParser(formatter_class=formatter)
    parser.add_argument("resource_group")
    parser.add_argument("storage_account")
    parser.add_argument("migration", choices=migrations.keys(), nargs="+")
    args = parser.parse_args()

    client = get_client_from_cli_profile(StorageManagementClient)
    storage_keys = client.storage_accounts.list_keys(
        args.resource_group, args.storage_account
    )
    table_service = TableService(
        account_name=args.storage_account, account_key=storage_keys.keys[0].value
    )
    print(args.migration)
    migrate(table_service, args.migration)


if __name__ == "__main__":
    main()
