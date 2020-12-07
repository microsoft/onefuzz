#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import datetime
import logging
import os
import urllib.parse
from typing import Dict, List, Optional, Union, cast

from azure.common import AzureHttpError, AzureMissingResourceHttpError
from azure.storage.blob import BlobPermissions, BlockBlobService, ContainerPermissions
from memoization import cached
from onefuzztypes.primitives import Container

from .storage import (
    StorageType,
    choose_account,
    get_accounts,
    get_storage_account_name_key,
)


@cached
def get_blob_service(account_id: str) -> BlockBlobService:
    logging.debug("getting blob container (account_id: %s)", account_id)
    account_name, account_key = get_storage_account_name_key(account_id)
    service = BlockBlobService(account_name=account_name, account_key=account_key)
    return service


def get_service_by_container(
    container: Container, storage_type: StorageType
) -> Optional[BlockBlobService]:
    account = get_account_by_container(container, storage_type)
    if account is None:
        return None
    service = get_blob_service(account)
    return service


def container_exists_on_account(container: Container, account_id: str) -> bool:
    try:
        get_blob_service(account_id).get_container_properties(container)
        return True
    except AzureHttpError:
        return False


def container_metadata(container: Container, account: str) -> Optional[Dict[str, str]]:
    try:
        result = get_blob_service(account).get_container_metadata(container)
        return cast(Dict[str, str], result)
    except AzureHttpError:
        pass
    return None


def get_account_by_container(
    container: Container, storage_type: StorageType
) -> Optional[str]:
    accounts = cast(List[str], get_accounts(storage_type))

    # check secondary accounts first by searching in reverse
    for account in reversed(accounts):
        if container_exists_on_account(container, account):
            return account
    return None


def container_exists(container: Container, storage_type: StorageType) -> bool:
    return get_account_by_container(container, storage_type) is not None


def get_containers(storage_type: StorageType) -> Dict[str, Dict[str, str]]:
    containers: Dict[str, Dict[str, str]] = {}

    for account_id in get_accounts(storage_type):
        containers.update(
            {
                x.name: x.metadata
                for x in get_blob_service(account_id).list_containers(
                    include_metadata=True
                )
            }
        )

    return containers


def get_container_metadata(
    container: Container, storage_type: StorageType
) -> Optional[Dict[str, str]]:
    account = get_account_by_container(container, storage_type)
    if account is None:
        return None

    return container_metadata(container, account)


def create_container(
    container: Container,
    storage_type: StorageType,
    metadata: Optional[Dict[str, str]],
) -> Optional[str]:
    service = get_service_by_container(container, storage_type)
    if service is None:
        account = choose_account(storage_type)
        service = get_blob_service(account)
        try:
            service.create_container(container, metadata=metadata)
        except AzureHttpError as err:
            logging.error(
                (
                    "unable to create container.  account: %s "
                    "container: %s metadata: %s - %s"
                ),
                account,
                container,
                metadata,
                err,
            )
            return None

    return get_container_sas_url_service(
        container,
        service,
        read=True,
        add=True,
        create=True,
        write=True,
        delete=True,
        list=True,
    )


def delete_container(container: Container, storage_type: StorageType) -> bool:
    accounts = cast(List[str], get_accounts(storage_type))
    for account in accounts:
        service = get_blob_service(account)
        if bool(service.delete_container(container)):
            return True

    return False


def get_container_sas_url_service(
    container: Container,
    service: BlockBlobService,
    *,
    read: bool = False,
    add: bool = False,
    create: bool = False,
    write: bool = False,
    delete: bool = False,
    list: bool = False,
) -> str:
    expiry = datetime.datetime.utcnow() + datetime.timedelta(days=30)
    permission = ContainerPermissions(read, add, create, write, delete, list)

    sas_token = service.generate_container_shared_access_signature(
        container, permission=permission, expiry=expiry
    )

    url = service.make_container_url(container, sas_token=sas_token)
    url = url.replace("?restype=container&", "?")
    return str(url)


def get_container_sas_url(
    container: Container,
    storage_type: StorageType,
    *,
    read: bool = False,
    add: bool = False,
    create: bool = False,
    write: bool = False,
    delete: bool = False,
    list: bool = False,
) -> str:
    service = get_service_by_container(container, storage_type)
    if not service:
        raise Exception("unable to create container sas for missing container")

    return get_container_sas_url_service(
        container,
        service,
        read=read,
        add=add,
        create=create,
        write=write,
        delete=delete,
        list=list,
    )


def get_file_sas_url(
    container: Container,
    name: str,
    storage_type: StorageType,
    *,
    read: bool = False,
    add: bool = False,
    create: bool = False,
    write: bool = False,
    delete: bool = False,
    list: bool = False,
    days: int = 30,
    hours: int = 0,
    minutes: int = 0,
) -> str:
    service = get_service_by_container(container, storage_type)
    if not service:
        raise Exception("unable to find container: %s - %s" % (container, storage_type))

    expiry = datetime.datetime.utcnow() + datetime.timedelta(
        days=days, hours=hours, minutes=minutes
    )
    permission = BlobPermissions(read, add, create, write, delete, list)

    sas_token = service.generate_blob_shared_access_signature(
        container, name, permission=permission, expiry=expiry
    )

    url = service.make_blob_url(container, name, sas_token=sas_token)
    return str(url)


def save_blob(
    container: Container,
    name: str,
    data: Union[str, bytes],
    storage_type: StorageType,
) -> None:
    service = get_service_by_container(container, storage_type)
    if not service:
        raise Exception("unable to find container: %s - %s" % (container, storage_type))

    if isinstance(data, str):
        service.create_blob_from_text(container, name, data)
    elif isinstance(data, bytes):
        service.create_blob_from_bytes(container, name, data)


def get_blob(
    container: Container, name: str, storage_type: StorageType
) -> Optional[bytes]:
    service = get_service_by_container(container, storage_type)
    if not service:
        return None

    try:
        blob = service.get_blob_to_bytes(container, name).content
        return cast(bytes, blob)
    except AzureMissingResourceHttpError:
        return None


def blob_exists(container: Container, name: str, storage_type: StorageType) -> bool:
    service = get_service_by_container(container, storage_type)
    if not service:
        return False

    try:
        service.get_blob_properties(container, name)
        return True
    except AzureMissingResourceHttpError:
        return False


def delete_blob(container: Container, name: str, storage_type: StorageType) -> bool:
    service = get_service_by_container(container, storage_type)
    if not service:
        return False

    try:
        service.delete_blob(container, name)
        return True
    except AzureMissingResourceHttpError:
        return False


def auth_download_url(container: Container, filename: str) -> str:
    instance = os.environ["ONEFUZZ_INSTANCE"]
    return "%s/api/download?%s" % (
        instance,
        urllib.parse.urlencode({"container": container, "filename": filename}),
    )
