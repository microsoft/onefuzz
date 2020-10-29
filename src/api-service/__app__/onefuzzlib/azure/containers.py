#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import datetime
import os
import urllib.parse
from typing import Any, Dict, Optional, Union, cast

from azure.common import AzureHttpError, AzureMissingResourceHttpError
from azure.storage.blob import BlobPermissions, ContainerPermissions
from memoization import cached

from .creds import get_blob_service


@cached(ttl=5)
def container_exists(name: str, *, account_id: str) -> bool:
    try:
        get_blob_service(account_id).get_container_properties(name)
        return True
    except AzureHttpError:
        return False


def get_containers(*, account_id: str) -> Dict[str, Dict[str, str]]:
    return {
        x.name: x.metadata
        for x in get_blob_service(account_id).list_containers(include_metadata=True)
        if not x.name.startswith("$")
    }


def get_container_metadata(name: str, *, account_id: str) -> Optional[Dict[str, str]]:
    try:
        result = get_blob_service(account_id).get_container_metadata(name)
        return cast(Dict[str, str], result)
    except AzureHttpError:
        pass
    return None


def create_container(
    name: str, *, metadata: Optional[Dict[str, str]], account_id: str
) -> Optional[str]:
    try:
        get_blob_service(account_id).create_container(name, metadata=metadata)
    except AzureHttpError:
        # azure storage already logs errors
        return None

    return get_container_sas_url(
        name,
        account_id=account_id,
        read=True,
        add=True,
        create=True,
        write=True,
        delete=True,
        list=True,
    )


def delete_container(name: str, *, account_id: str) -> bool:
    try:
        return bool(get_blob_service(account_id).delete_container(name))
    except AzureHttpError:
        # azure storage already logs errors
        return False


def get_container_sas_url(
    container: str,
    *,
    account_id: str,
    read: bool = False,
    add: bool = False,
    create: bool = False,
    write: bool = False,
    delete: bool = False,
    list: bool = False,
) -> str:
    service = get_blob_service(account_id)
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
    *,
    account_id: str,
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
    service = get_blob_service(account_id)
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
    container: str, name: str, data: Union[str, bytes], *, account_id: str
) -> None:
    service = get_blob_service(account_id)
    service.create_container(container)
    if isinstance(data, str):
        service.create_blob_from_text(container, name, data)
    elif isinstance(data, bytes):
        service.create_blob_from_bytes(container, name, data)


def get_blob(
    container: str, name: str, *, account_id: str
) -> Optional[Any]:  # should be bytes
    service = get_blob_service(account_id)
    try:
        blob = service.get_blob_to_bytes(container, name).content
        return blob
    except AzureMissingResourceHttpError:
        return None


def blob_exists(container: str, name: str, *, account_id: str) -> bool:
    service = get_blob_service(account_id)
    try:
        service.get_blob_properties(container, name)
        return True
    except AzureMissingResourceHttpError:
        return False


def delete_blob(container: str, name: str, *, account_id: str) -> bool:
    service = get_blob_service(account_id)
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
