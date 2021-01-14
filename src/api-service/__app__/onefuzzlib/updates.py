#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from typing import Dict, Optional, Type

from azure.core.exceptions import ResourceNotFoundError
from msrestazure.azure_exceptions import CloudError
from onefuzztypes.enums import UpdateType
from pydantic import BaseModel

from .azure.queue import queue_object
from .azure.storage import StorageType


# This class isn't intended to be shared outside of the service
class Update(BaseModel):
    update_type: UpdateType
    PartitionKey: Optional[str]
    RowKey: Optional[str]
    method: Optional[str]


def queue_update(
    update_type: UpdateType,
    PartitionKey: Optional[str] = None,
    RowKey: Optional[str] = None,
    method: Optional[str] = None,
    visibility_timeout: int = None,
) -> None:
    logging.info(
        "queuing type:%s id:[%s,%s] method:%s timeout: %s",
        update_type.name,
        PartitionKey,
        RowKey,
        method,
        visibility_timeout,
    )

    update = Update(
        update_type=update_type, PartitionKey=PartitionKey, RowKey=RowKey, method=method
    )

    try:
        if not queue_object(
            "update-queue",
            update,
            StorageType.config,
            visibility_timeout=visibility_timeout,
        ):
            logging.error("unable to queue update")
    except (CloudError, ResourceNotFoundError) as err:
        logging.error("GOT ERROR %s", repr(err))
        logging.error("GOT ERROR %s", dir(err))
        raise err


def execute_update(update: Update) -> None:
    from .jobs import Job
    from .orm import ORMMixin
    from .pools import Node, Pool, Scaleset
    from .proxy import Proxy
    from .repro import Repro
    from .tasks.main import Task

    update_objects: Dict[UpdateType, Type[ORMMixin]] = {
        UpdateType.Task: Task,
        UpdateType.Job: Job,
        UpdateType.Repro: Repro,
        UpdateType.Proxy: Proxy,
        UpdateType.Pool: Pool,
        UpdateType.Node: Node,
        UpdateType.Scaleset: Scaleset,
    }

    # TODO: remove these from being queued, these updates are handled elsewhere
    if update.update_type == UpdateType.Scaleset:
        return

    if update.update_type in update_objects:
        if update.PartitionKey is None or update.RowKey is None:
            raise Exception("unsupported update: %s" % update)

        obj = update_objects[update.update_type].get(update.PartitionKey, update.RowKey)
        if not obj:
            logging.error("unable find to obj to update %s", update)
            return

        if update.method and hasattr(obj, update.method):
            logging.info("performing queued update: %s", update)
            getattr(obj, update.method)()
            return
        else:
            state = getattr(obj, "state", None)
            if state is None:
                logging.error("queued update for object without state: %s", update)
                return
            func = getattr(obj, state.name, None)
            if func is None:
                logging.debug(
                    "no function to implement state: %s - %s", update, state.name
                )
                return
            logging.info(
                "performing queued update for state: %s - %s", update, state.name
            )
            func()
        return

    raise NotImplementedError("unimplemented update type: %s" % update.update_type.name)
