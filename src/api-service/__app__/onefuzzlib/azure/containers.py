#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import datetime
import logging
import os
import urllib.parse
from typing import Dict, Optional, Tuple, Union, cast

from azure.common import AzureHttpError, AzureMissingResourceHttpError
from azure.core.exceptions import ResourceExistsError, ResourceNotFoundError
from azure.storage.blob import (
    BlobClient,
    BlobSasPermissions,
    BlobServiceClient,
    ContainerClient,
    ContainerSasPermissions,
    generate_blob_sas,
    generate_container_sas,
)
from memoization import cached
from onefuzztypes.primitives import Container

from .storage import (
    StorageType,
    choose_account,
    get_accounts,
    get_storage_account_name_key,
    get_storage_account_name_key_by_name,
)

CONTAINER_SAS_DEFAULT_DURATION = datetime.timedelta(days=30)


def get_url(account_name: str) -> str:
    return f"https://{account_name}.blob.core.windows.net/"


@cached
def get_blob_service(account_id: str) -> BlobServiceClient:
    logging.debug("getting blob container (account_id: %s)", account_id)
    account_name, account_key = get_storage_account_name_key(account_id)
    account_url = get_url(account_name)
    service = BlobServiceClient(account_url=account_url, credential=account_key)
    return service


def container_metadata(
    container: Container, account_id: str
) -> Optional[Dict[str, str]]:
    try:
        result = (
            get_blob_service(account_id)
            .get_container_client(container)
            .get_container_properties()
        )
        return cast(Dict[str, str], result)
    except AzureHttpError:
        pass
    return None


def find_container(
    container: Container, storage_type: StorageType
) -> Optional[ContainerClient]:
    accounts = get_accounts(storage_type)

    # check secondary accounts first by searching in reverse.
    #
    # By implementation, the primary account is specified first, followed by
    # any secondary accounts.
    #
    # Secondary accounts, if they exist, are preferred for containers and have
    # increased IOP rates, this should be a slight optimization
    for account in reversed(accounts):
        client = get_blob_service(account).get_container_client(container)
        if client.exists():
            return client
    return None


def container_exists(container: Container, storage_type: StorageType) -> bool:
    return find_container(container, storage_type) is not None


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
    client = find_container(container, storage_type)
    if client is None:
        return None
    result = client.get_container_properties().metadata
    return cast(Dict[str, str], result)


def add_container_sas_url(
    container_url: str, duration: datetime.timedelta = CONTAINER_SAS_DEFAULT_DURATION
) -> str:
    parsed = urllib.parse.urlparse(container_url)
    query = urllib.parse.parse_qs(parsed.query)
    if "sig" in query:
        return container_url
    else:
        start, expiry = sas_time_window(duration)
        account_name = parsed.netloc.split(".")[0]
        account_key = get_storage_account_name_key_by_name(account_name)
        sas_token = generate_container_sas(
            account_name=account_name,
            container_name=parsed.path.split("/")[1],
            account_key=account_key,
            permission=ContainerSasPermissions(
                read=True, write=True, delete=True, list=True
            ),
            expiry=expiry,
            start=start,
        )
        return f"{container_url}?{sas_token}"


def get_or_create_container_client(
    container: Container,
    storage_type: StorageType,
    metadata: Optional[Dict[str, str]],
) -> Optional[ContainerClient]:
    client = find_container(container, storage_type)
    if client is None:
        account = choose_account(storage_type)
        client = get_blob_service(account).get_container_client(container)
        try:
            client.create_container(metadata=metadata)
        except (ResourceExistsError, AzureHttpError) as err:
            # note: resource exists error happens during creation if the container
            # is being deleted
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
    return client


def create_container(
    container: Container,
    storage_type: StorageType,
    metadata: Optional[Dict[str, str]],
) -> Optional[str]:
    client = get_or_create_container_client(container, storage_type, metadata)
    if client is None:
        return None
    return get_container_sas_url_service(
        client,
        read=True,
        write=True,
        delete=True,
        list_=True,
    )


def delete_container(container: Container, storage_type: StorageType) -> bool:
    accounts = get_accounts(storage_type)
    deleted = False
    for account in accounts:
        service = get_blob_service(account)
        try:
            service.delete_container(container)
            deleted = True
        except ResourceNotFoundError:
            pass

    return deleted


def sas_time_window(
    duration: datetime.timedelta,
) -> Tuple[datetime.datetime, datetime.datetime]:
    # SAS URLs are valid 6 hours earlier, primarily to work around dev
    # workstations having out-of-sync time.  Additionally, SAS URLs are stopped
    # 15 minutes later than requested based on "Be careful with SAS start time"
    # guidance.
    # Ref: https://docs.microsoft.com/en-us/azure/storage/common/storage-sas-overview
    SAS_START_TIME_DELTA = datetime.timedelta(hours=6)
    SAS_END_TIME_DELTA = datetime.timedelta(minutes=15)

    now = datetime.datetime.utcnow()
    start = now - SAS_START_TIME_DELTA
    expiry = now + duration + SAS_END_TIME_DELTA
    return (start, expiry)


def get_container_sas_url_service(
    client: ContainerClient,
    *,
    read: bool = False,
    write: bool = False,
    delete: bool = False,
    list_: bool = False,
    delete_previous_version: bool = False,
    tag: bool = False,
    duration: datetime.timedelta = CONTAINER_SAS_DEFAULT_DURATION,
) -> str:
    account_name = client.account_name
    container_name = client.container_name
    account_key = get_storage_account_name_key_by_name(account_name)

    start, expiry = sas_time_window(duration)

    sas = generate_container_sas(
        account_name,
        container_name,
        account_key=account_key,
        permission=ContainerSasPermissions(
            read=read,
            write=write,
            delete=delete,
            list=list_,
            delete_previous_version=delete_previous_version,
            tag=tag,
        ),
        start=start,
        expiry=expiry,
    )

    with_sas = ContainerClient(
        get_url(account_name),
        container_name=container_name,
        credential=sas,
    )
    return cast(str, with_sas.url)


def get_container_sas_url(
    container: Container,
    storage_type: StorageType,
    *,
    read: bool = False,
    write: bool = False,
    delete: bool = False,
    list_: bool = False,
) -> str:
    client = find_container(container, storage_type)
    if not client:
        raise Exception("unable to create container sas for missing container")

    return get_container_sas_url_service(
        client,
        read=read,
        write=write,
        delete=delete,
        list_=list_,
    )


def get_file_url(container: Container, name: str, storage_type: StorageType) -> str:
    client = find_container(container, storage_type)
    if not client:
        raise Exception("unable to find container: %s - %s" % (container, storage_type))

    # get_url has a trailing '/'
    return f"{get_url(client.account_name)}{container}/{name}"


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
    delete_previous_version: bool = False,
    tag: bool = False,
    duration: datetime.timedelta = CONTAINER_SAS_DEFAULT_DURATION,
) -> str:
    client = find_container(container, storage_type)
    if not client:
        raise Exception("unable to find container: %s - %s" % (container, storage_type))

    account_key = get_storage_account_name_key_by_name(client.account_name)
    start, expiry = sas_time_window(duration)

    permission = BlobSasPermissions(
        read=read,
        add=add,
        create=create,
        write=write,
        delete=delete,
        delete_previous_version=delete_previous_version,
        tag=tag,
    )
    sas = generate_blob_sas(
        client.account_name,
        container,
        name,
        account_key=account_key,
        permission=permission,
        expiry=expiry,
        start=start,
    )

    with_sas = BlobClient(
        get_url(client.account_name),
        container,
        name,
        credential=sas,
    )
    return cast(str, with_sas.url)


def save_blob(
    container: Container,
    name: str,
    data: Union[str, bytes],
    storage_type: StorageType,
) -> None:
    client = find_container(container, storage_type)
    if not client:
        raise Exception("unable to find container: %s - %s" % (container, storage_type))

    client.get_blob_client(name).upload_blob(data, overwrite=True)


def get_blob(
    container: Container, name: str, storage_type: StorageType
) -> Optional[bytes]:
    client = find_container(container, storage_type)
    if not client:
        return None

    try:
        return cast(
            bytes, client.get_blob_client(name).download_blob().content_as_bytes()
        )
    except AzureMissingResourceHttpError:
        return None


def blob_exists(container: Container, name: str, storage_type: StorageType) -> bool:
    client = find_container(container, storage_type)
    if not client:
        return False

    return cast(bool, client.get_blob_client(name).exists())


def delete_blob(container: Container, name: str, storage_type: StorageType) -> bool:
    client = find_container(container, storage_type)
    if not client:
        return False

    try:
        client.get_blob_client(name).delete_blob()
        return True
    except AzureMissingResourceHttpError:
        return False


def auth_download_url(container: Container, filename: str) -> str:
    instance = os.environ["ONEFUZZ_INSTANCE"]
    return "%s/api/download?%s" % (
        instance,
        urllib.parse.urlencode({"container": container, "filename": filename}),
    )
