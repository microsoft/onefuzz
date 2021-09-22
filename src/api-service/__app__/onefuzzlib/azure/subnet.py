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
from onefuzztypes.models import Error, NetworkConfig
from onefuzztypes.primitives import Region

from .network_mgmt_client import get_network_client


def get_subnet_id(resource_group: str, name: str, region: Region) -> Optional[str]:
    network_client = get_network_client()
    try:
        subnet = network_client.subnets.get(resource_group, name, region)
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
    resource_group: str,
    name: str,
    region: Region,
    network_config: NetworkConfig,
) -> Optional[Error]:
    logging.info(
        "creating subnet - resource group:%s name:%s region:%s",
        resource_group,
        name,
        region,
    )

    network_client = get_network_client()
    params = {
        "location": region,
        "address_space": {"address_prefixes": [network_config.address_space]},
        "subnets": [{"name": name, "address_prefix": network_config.subnet}],
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
