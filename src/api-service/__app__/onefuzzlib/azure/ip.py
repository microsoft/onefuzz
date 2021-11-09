#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
import os
from typing import Any, Dict, Optional, Union
from uuid import UUID

from azure.core.exceptions import ResourceNotFoundError
from azure.mgmt.network.models import Subnet
from msrestazure.azure_exceptions import CloudError
from msrestazure.tools import parse_resource_id
from onefuzztypes.enums import ErrorCode
from onefuzztypes.models import Error
from onefuzztypes.primitives import Region

from .creds import get_base_resource_group
from .network import Network
from .network_mgmt_client import get_network_client
from .nsg import NSG
from .vmss import get_instance_id


def get_scaleset_instance_ip(scaleset: UUID, machine_id: UUID) -> Optional[str]:
    instance = get_instance_id(scaleset, machine_id)
    if not isinstance(instance, str):
        return None

    resource_group = get_base_resource_group()

    client = get_network_client()
    intf = client.network_interfaces.list_virtual_machine_scale_set_network_interfaces(
        resource_group, str(scaleset)
    )
    try:
        for interface in intf:
            resource = parse_resource_id(interface.virtual_machine.id)
            if resource.get("resource_name") != instance:
                continue

            for config in interface.ip_configurations:
                if config.private_ip_address is None:
                    continue
                return str(config.private_ip_address)
    except (ResourceNotFoundError, CloudError):
        # this can fail if an interface is removed during the iteration
        pass

    return None


def get_ip(resource_group: str, name: str) -> Optional[Any]:
    logging.info("getting ip %s:%s", resource_group, name)
    network_client = get_network_client()
    try:
        return network_client.public_ip_addresses.get(resource_group, name)
    except (ResourceNotFoundError, CloudError):
        return None


def delete_ip(resource_group: str, name: str) -> Any:
    logging.info("deleting ip %s:%s", resource_group, name)
    network_client = get_network_client()
    return network_client.public_ip_addresses.begin_delete(resource_group, name)


def create_ip(resource_group: str, name: str, region: Region) -> Any:
    logging.info("creating ip for %s:%s in %s", resource_group, name, region)

    network_client = get_network_client()
    params: Dict[str, Union[str, Dict[str, str]]] = {
        "location": region,
        "public_ip_allocation_method": "Dynamic",
    }
    if "ONEFUZZ_OWNER" in os.environ:
        params["tags"] = {"OWNER": os.environ["ONEFUZZ_OWNER"]}
    return network_client.public_ip_addresses.begin_create_or_update(
        resource_group, name, params
    )


def get_public_nic(resource_group: str, name: str) -> Optional[Any]:
    logging.info("getting  nic: %s %s", resource_group, name)
    network_client = get_network_client()
    try:
        return network_client.network_interfaces.get(resource_group, name)
    except (ResourceNotFoundError, CloudError):
        return None


def delete_nic(resource_group: str, name: str) -> Optional[Any]:
    logging.info("deleting nic %s:%s", resource_group, name)
    network_client = get_network_client()
    return network_client.network_interfaces.begin_delete(resource_group, name)


def create_public_nic(
    resource_group: str, name: str, region: Region, nsg: Optional[NSG]
) -> Optional[Error]:
    logging.info("creating nic for %s:%s in %s", resource_group, name, region)

    network = Network(region)
    subnet_id = network.get_id()
    if subnet_id is None:
        network.create()
        return None

    if nsg:
        subnet = network.get_subnet()
        if isinstance(subnet, Subnet) and not subnet.network_security_group:
            result = nsg.associate_subnet(network.get_vnet(), subnet)
            if isinstance(result, Error):
                return result
            return None

    ip = get_ip(resource_group, name)
    if not ip:
        create_ip(resource_group, name, region)
        return None

    params = {
        "location": region,
        "ip_configurations": [
            {
                "name": "myIPConfig",
                "public_ip_address": ip,
                "subnet": {"id": subnet_id},
            }
        ],
    }
    if "ONEFUZZ_OWNER" in os.environ:
        params["tags"] = {"OWNER": os.environ["ONEFUZZ_OWNER"]}

    network_client = get_network_client()
    try:
        network_client.network_interfaces.begin_create_or_update(
            resource_group, name, params
        )
    except (ResourceNotFoundError, CloudError) as err:
        if "RetryableError" not in repr(err):
            return Error(
                code=ErrorCode.VM_CREATE_FAILED,
                errors=["unable to create nic: %s" % err],
            )
    return None


def get_public_ip(resource_id: str) -> Optional[str]:
    logging.info("getting ip for %s", resource_id)
    network_client = get_network_client()
    resource = parse_resource_id(resource_id)
    ip = (
        network_client.network_interfaces.get(
            resource["resource_group"], resource["name"]
        )
        .ip_configurations[0]
        .public_ip_address
    )
    resource = parse_resource_id(ip.id)
    ip = network_client.public_ip_addresses.get(
        resource["resource_group"], resource["name"]
    ).ip_address
    if ip is None:
        return None
    else:
        return str(ip)
