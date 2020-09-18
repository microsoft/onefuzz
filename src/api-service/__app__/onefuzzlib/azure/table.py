#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
import os
from typing import Optional

from azure.cosmosdb.table import TableService
from memoization import cached

from .creds import get_storage_account_name_key


@cached(ttl=60)
def get_client(
    table: Optional[str] = None, account_id: Optional[str] = None
) -> TableService:
    if account_id is None:
        account_id = os.environ["ONEFUZZ_FUNC_STORAGE"]

    logging.info("getting table account: (account_id: %s)", account_id)
    name, key = get_storage_account_name_key(account_id)
    client = TableService(account_name=name, account_key=key)

    if table and not client.exists(table):
        logging.info("creating missing table %s", table)
        client.create_table(table, fail_on_exist=False)
    return client
