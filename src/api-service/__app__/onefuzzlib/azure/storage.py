import logging
import os
import random
from enum import Enum
from typing import List, Tuple

from azure.mgmt.storage import StorageManagementClient
from memoization import cached
from msrestazure.tools import parse_resource_id

from .creds import get_base_resource_group, mgmt_client_factory


class StorageType(Enum):
    corpus = "corpus"
    config = "config"


@cached
def get_fuzz_storage() -> str:
    return os.environ["ONEFUZZ_DATA_STORAGE"]


@cached
def get_func_storage() -> str:
    return os.environ["ONEFUZZ_FUNC_STORAGE"]


@cached
def get_primary_account(storage_type: StorageType) -> str:
    if storage_type == StorageType.corpus:
        # see #322 for discussion about typing
        return get_fuzz_storage()
    elif storage_type == StorageType.config:
        # see #322 for discussion about typing
        return get_func_storage()
    raise NotImplementedError


@cached
def get_accounts(storage_type: StorageType) -> List[str]:
    if storage_type == StorageType.corpus:
        return corpus_accounts()
    elif storage_type == StorageType.config:
        return [get_func_storage()]
    else:
        raise NotImplementedError


@cached
def get_storage_account_name_key(account_id: str) -> Tuple[str, str]:
    client = mgmt_client_factory(StorageManagementClient)
    resource = parse_resource_id(account_id)
    key = (
        client.storage_accounts.list_keys(resource["resource_group"], resource["name"])
        .keys[0]
        .value
    )
    return resource["name"], key


def choose_account(storage_type: StorageType) -> str:
    accounts = get_accounts(storage_type)
    if not accounts:
        raise Exception(f"no storage accounts for {storage_type}")

    if len(accounts) == 1:
        return accounts[0]

    # Use a random secondary storage account if any are available
    return random.choice(accounts[1:])


@cached
def corpus_accounts() -> List[str]:
    skip = get_func_storage()
    results = [get_fuzz_storage()]

    client = mgmt_client_factory(StorageManagementClient)
    group = get_base_resource_group()
    for account in client.storage_accounts.list_by_resource_group(group):
        # protection from someone adding the corpus tag to the config account
        if account.id == skip:
            continue

        if account.id in results:
            continue

        if account.primary_endpoints.blob is None:
            continue

        if (
            "storage_type" not in account.tags
            or account.tags["storage_type"] != "corpus"
        ):
            continue

        results.append(account.id)

    logging.info("corpus accounts: %s", corpus_accounts)
    return results
