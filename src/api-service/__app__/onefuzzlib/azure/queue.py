#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import base64
import datetime
import json
import logging
from typing import List, Optional, Type, TypeVar, Union
from uuid import UUID

from azure.core.exceptions import ResourceExistsError, ResourceNotFoundError
from azure.storage.queue import (
    QueueSasPermissions,
    QueueServiceClient,
    generate_queue_sas,
)
from memoization import cached
from pydantic import BaseModel

from .storage import StorageType, get_primary_account, get_storage_account_name_key

QueueNameType = Union[str, UUID]

DEFAULT_TTL = -1
DEFAULT_DURATION = datetime.timedelta(days=30)


@cached(ttl=60)
def get_queue_client(storage_type: StorageType) -> QueueServiceClient:
    account_id = get_primary_account(storage_type)
    logging.debug("getting blob container (account_id: %s)", account_id)
    name, key = get_storage_account_name_key(account_id)
    account_url = "https://%s.queue.core.windows.net" % name
    client = QueueServiceClient(
        account_url=account_url,
        credential={"account_name": name, "account_key": key},
    )
    return client


@cached(ttl=60)
def get_queue_sas(
    queue: QueueNameType,
    storage_type: StorageType,
    *,
    read: bool = False,
    add: bool = False,
    update: bool = False,
    process: bool = False,
    duration: Optional[datetime.timedelta] = None,
) -> str:
    if duration is None:
        duration = DEFAULT_DURATION
    account_id = get_primary_account(storage_type)
    logging.debug("getting queue sas %s (account_id: %s)", queue, account_id)
    name, key = get_storage_account_name_key(account_id)
    expiry = datetime.datetime.utcnow() + duration

    token = generate_queue_sas(
        name,
        str(queue),
        key,
        permission=QueueSasPermissions(
            read=read, add=add, update=update, process=process
        ),
        expiry=expiry,
    )

    url = "https://%s.queue.core.windows.net/%s?%s" % (name, queue, token)
    return url


@cached(ttl=60)
def create_queue(name: QueueNameType, storage_type: StorageType) -> None:
    client = get_queue_client(storage_type)
    try:
        client.create_queue(str(name))
    except ResourceExistsError:
        pass


def delete_queue(name: QueueNameType, storage_type: StorageType) -> None:
    client = get_queue_client(storage_type)
    queues = client.list_queues()
    if str(name) in [x["name"] for x in queues]:
        client.delete_queue(name)


def get_queue(
    name: QueueNameType, storage_type: StorageType
) -> Optional[QueueServiceClient]:
    client = get_queue_client(storage_type)
    try:
        return client.get_queue_client(str(name))
    except ResourceNotFoundError:
        return None


def clear_queue(name: QueueNameType, storage_type: StorageType) -> None:
    queue = get_queue(name, storage_type)
    if queue:
        try:
            queue.clear_messages()
        except ResourceNotFoundError:
            pass


def send_message(
    name: QueueNameType,
    message: bytes,
    storage_type: StorageType,
    *,
    visibility_timeout: Optional[int] = None,
    time_to_live: int = DEFAULT_TTL,
) -> None:
    queue = get_queue(name, storage_type)
    if queue:
        try:
            queue.send_message(
                base64.b64encode(message).decode(),
                visibility_timeout=visibility_timeout,
                time_to_live=time_to_live,
            )
        except ResourceNotFoundError:
            pass


def remove_first_message(name: QueueNameType, storage_type: StorageType) -> bool:
    queue = get_queue(name, storage_type)
    if queue:
        try:
            for message in queue.receive_messages():
                queue.delete_message(message)
                return True
        except ResourceNotFoundError:
            return False
    return False


A = TypeVar("A", bound=BaseModel)


MIN_PEEK_SIZE = 1
MAX_PEEK_SIZE = 32


# Peek at a max of 32 messages
# https://docs.microsoft.com/en-us/python/api/azure-storage-queue/azure.storage.queue.queueclient
def peek_queue(
    name: QueueNameType,
    storage_type: StorageType,
    *,
    object_type: Type[A],
    max_messages: int = MAX_PEEK_SIZE,
) -> List[A]:
    result: List[A] = []

    # message count
    if max_messages < MIN_PEEK_SIZE or max_messages > MAX_PEEK_SIZE:
        raise ValueError("invalid max messages: %s" % max_messages)

    try:
        queue = get_queue(name, storage_type)
        if not queue:
            return result

        for message in queue.peek_messages(max_messages=max_messages):
            decoded = base64.b64decode(message.content)
            raw = json.loads(decoded)
            result.append(object_type.parse_obj(raw))
    except ResourceNotFoundError:
        return result
    return result


def queue_object(
    name: QueueNameType,
    message: BaseModel,
    storage_type: StorageType,
    *,
    visibility_timeout: Optional[int] = None,
    time_to_live: int = DEFAULT_TTL,
) -> bool:
    queue = get_queue(name, storage_type)
    if not queue:
        raise Exception("unable to queue object, no such queue: %s" % queue)

    encoded = base64.b64encode(message.json(exclude_none=True).encode()).decode()
    try:
        queue.send_message(
            encoded, visibility_timeout=visibility_timeout, time_to_live=time_to_live
        )
        return True
    except ResourceNotFoundError:
        return False


def get_resource_id(queue_name: QueueNameType, storage_type: StorageType) -> str:
    account_id = get_primary_account(storage_type)
    resource_uri = "%s/services/queue/queues/%s" % (account_id, queue_name)
    return resource_uri
