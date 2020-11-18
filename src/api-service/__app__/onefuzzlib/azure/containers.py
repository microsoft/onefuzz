#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import datetime
import os
import urllib.parse
from enum import Enum
from typing import Any, Dict, Optional, Union, cast

from azure.common import AzureHttpError, AzureMissingResourceHttpError
from azure.storage.blob import BlobPermissions, ContainerPermissions
from memoization import cached

from .creds import get_blob_service, get_func_storage, get_fuzz_storage


class StorageType(Enum):
    corpus = "corpus"
    config = "config"


def get_account_id_by_type(storage_type: StorageType) -> str:
    if storage_type == StorageType.corpus:
        account_id = get_fuzz_storage()
    elif storage_type == StorageType.config:
        account_id = get_func_storage()
    else:
        raise NotImplementedError
    return account_id


@cached(ttl=5)
def get_blob_service_by_type(storage_type: StorageType) -> Any:
    account_id = get_account_id_by_type(storage_type)
    return get_blob_service(account_id)


@cached(ttl=5)
def container_exists(name: str, storage_type: StorageType) -> bool:
    try:
        get_blob_service_by_type(storage_type).get_container_properties(name)
        return True
    except AzureHttpError:
        return False


def get_containers(storage_type: StorageType) -> Dict[str, Dict[str, str]]:
    return {
        x.name: x.metadata
        for x in get_blob_service_by_type(storage_type).list_containers(
            include_metadata=True
        )
        if not x.name.startswith("$")
    }


def get_container_metadata(
    name: str, storage_type: StorageType
) -> Optional[Dict[str, str]]:
    try:
        result = get_blob_service_by_type(storage_type).get_container_metadata(name)
        return cast(Dict[str, str], result)
    except AzureHttpError:
        pass
    return None


def create_container(
    name: str, storage_type: StorageType, metadata: Optional[Dict[str, str]]
) -> Optional[str]:
    try:
        get_blob_service_by_type(storage_type).create_container(name, metadata=metadata)
    except AzureHttpError:
        # azure storage already logs errors
        return None

    return get_container_sas_url(
        name,
        storage_type,
        read=True,
        add=True,
        create=True,
        write=True,
        delete=True,
        list=True,
    )


def delete_container(name: str, storage_type: StorageType) -> bool:
    try:
        return bool(get_blob_service_by_type(storage_type).delete_container(name))
    except AzureHttpError:
        # azure storage already logs errors
        return False


def get_container_sas_url(
    container: str,
    storage_type: StorageType,
    *,
    read: bool = False,
    add: bool = False,
    create: bool = False,
    write: bool = False,
    delete: bool = False,
    list: bool = False,
) -> str:
    service = get_blob_service_by_type(storage_type)
    expiry = datetime.datetime.utcnow() + datetime.timedelta(days=30)
    permission = ContainerPermissions(read, add, create, write, delete, list)

    sas_token = service.generate_container_shared_access_signature(
        container, permission=permission, expiry=expiry
    )

    url = service.make_container_url(container, sas_token=sas_token)
    url = url.replace("?restype=container&", "?")
    return str(url)


def get_file_sas_url(
    container: str,
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
    service = get_blob_service_by_type(storage_type)
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
    container: str, name: str, data: Union[str, bytes], storage_type: StorageType
) -> None:
    service = get_blob_service_by_type(storage_type)
    service.create_container(container)
    if isinstance(data, str):
        service.create_blob_from_text(container, name, data)
    elif isinstance(data, bytes):
        service.create_blob_from_bytes(container, name, data)


def get_blob(container: str, name: str, storage_type: StorageType) -> Optional[bytes]:
    service = get_blob_service_by_type(storage_type)
    try:
        blob = service.get_blob_to_bytes(container, name).content
        return cast(bytes, blob)
    except AzureMissingResourceHttpError:
        return None


def blob_exists(container: str, name: str, storage_type: StorageType) -> bool:
    service = get_blob_service_by_type(storage_type)
    try:
        service.get_blob_properties(container, name)
        return True
    except AzureMissingResourceHttpError:
        return False


def delete_blob(container: str, name: str, storage_type: StorageType) -> bool:
    service = get_blob_service_by_type(storage_type)
    try:
        service.delete_blob(container, name)
        return True
    except AzureMissingResourceHttpError:
        return False


def auth_download_url(container: str, filename: str) -> str:
    instance = os.environ["ONEFUZZ_INSTANCE"]
    return "%s/api/download?%s" % (
        instance,
        urllib.parse.urlencode({"container": container, "filename": filename}),
    )
