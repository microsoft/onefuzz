#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
import os
from typing import Any, Optional, Union, cast

from azure.mgmt.network import NetworkManagementClient
from msrestazure.azure_exceptions import CloudError
from onefuzztypes.enums import ErrorCode
from onefuzztypes.models import Error

from .creds import mgmt_client_factory


def get_subnet_id(resource_group: str, name: str) -> Optional[str]:
    network_client = mgmt_client_factory(NetworkManagementClient)
    try:
        subnet = network_client.subnets.get(resource_group, name, name)
        return cast(str, subnet.id)
    except CloudError:
        logging.info(
            "subnet missing: resource group: %s name: %s",
            resource_group,
            name,
        )
    return None


def delete_subnet(resource_group: str, name: str) -> Union[None, CloudError, Any]:
    network_client = mgmt_client_factory(NetworkManagementClient)
    try:
        return network_client.virtual_networks.delete(resource_group, name)
    except CloudError as err:
        if err.error and "InUseSubnetCannotBeDeleted" in str(err.error):
            logging.error(
                "subnet delete failed: %s %s : %s", resource_group, name, repr(err)
            )
            return None
        else:
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

    network_client = mgmt_client_factory(NetworkManagementClient)
    params = {
        "location": location,
        "address_space": {"address_prefixes": ["10.0.0.0/8"]},
        "subnets": [{"name": name, "address_prefix": "10.0.0.0/16"}],
    }
    if "ONEFUZZ_OWNER" in os.environ:
        params["tags"] = {"OWNER": os.environ["ONEFUZZ_OWNER"]}

    try:
        network_client.virtual_networks.create_or_update(resource_group, name, params)
    except CloudError as err:
        return Error(code=ErrorCode.UNABLE_TO_CREATE_NETWORK, errors=[str(err.message)])

    return None
