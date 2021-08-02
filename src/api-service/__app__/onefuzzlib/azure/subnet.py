#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
import os
from typing import Any, Optional, Union, cast

from azure.core.exceptions import ResourceNotFoundError
from msrestazure.azure_exceptions import CloudError
from onefuzztypes.enums import ErrorCode
from onefuzztypes.models import Error

from .network_mgmt_client import get_network_client


def get_subnet_id(resource_group: str, name: str) -> Optional[str]:
    network_client = get_network_client()
    try:
        subnet = network_client.subnets.get(resource_group, name, name)
        return cast(str, subnet.id)
    except (CloudError, ResourceNotFoundError):
        logging.info(
            "subnet missing: resource group: %s name: %s",
            resource_group,
            name,
        )
    return None


def delete_subnet(resource_group: str, name: str) -> Union[None, CloudError, Any]:
    network_client = get_network_client()
    try:
        return network_client.virtual_networks.begin_delete(resource_group, name)
    except (CloudError, ResourceNotFoundError) as err:
        if err.error and "InUseSubnetCannotBeDeleted" in str(err.error):
            logging.error(
                "subnet delete failed: %s %s : %s", resource_group, name, repr(err)
            )
            return None
        raise err


def create_virtual_network(
    resource_group: str, name: str, location: str
) -> Optional[Error]:
    logging.info(
        "creating subnet - resource group: %s name: %s location: %s",
        resource_group,
        name,
        location,
    )

    network_client = get_network_client()
    params = {
        "location": location,
        "address_space": {"address_prefixes": ["10.0.0.0/8"]},
        "subnets": [{"name": name, "address_prefix": "10.0.0.0/16"}],
    }
    if "ONEFUZZ_OWNER" in os.environ:
        params["tags"] = {"OWNER": os.environ["ONEFUZZ_OWNER"]}

    try:
        network_client.virtual_networks.begin_create_or_update(
            resource_group, name, params
        )
    except (CloudError, ResourceNotFoundError) as err:
        return Error(code=ErrorCode.UNABLE_TO_CREATE_NETWORK, errors=[str(err)])

    return None
