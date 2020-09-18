from azure.cosmosdb.table.tableservice import TableService
from azure.cosmosdb.table.models import Entity
from azure.cosmosdb.table.tablebatch import TableBatch
import json
from typing import Optional, Callable, Dict, List


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


migrations: Dict[str, Callable[[TableService], None]] = {
    "migrate_task_os": migrate_task_os
}


def migrate(table_service: TableService, migration_names: List[str]) -> None:
    for name in migration_names:
        print("applying migration '%s'" % name)
        migrations[name](table_service)
        print("migration '%s' applied" % name)
