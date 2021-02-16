#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from typing import Any

from azure.core.exceptions import ResourceNotFoundError
from msrestazure.azure_exceptions import CloudError

from .compute import get_compute_client


def list_disks(resource_group: str) -> Any:
    logging.info("listing disks %s", resource_group)
    compute_client = get_compute_client()
    return compute_client.disks.list_by_resource_group(resource_group)


def delete_disk(resource_group: str, name: str) -> bool:
    logging.info("deleting disks %s : %s", resource_group, name)
    compute_client = get_compute_client()
    try:
        compute_client.disks.begin_delete(resource_group, name)
        return True
    except (ResourceNotFoundError, CloudError) as err:
        logging.error("unable to delete disk: %s", err)
    return False
