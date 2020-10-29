#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
import os
from typing import Any, Dict, List, Optional, Union, cast
from uuid import UUID

from azure.mgmt.compute import ComputeManagementClient
from azure.mgmt.compute.models import ResourceSku, ResourceSkuRestrictionsType
from memoization import cached
from msrestazure.azure_exceptions import CloudError
from onefuzztypes.enums import OS, ErrorCode
from onefuzztypes.models import Error
from onefuzztypes.primitives import Region

from .creds import (
    get_base_resource_group,
    get_scaleset_identity_resource_path,
    mgmt_client_factory,
)
from .image import get_os


def list_vmss(name: UUID) -> Optional[List[str]]:
    resource_group = get_base_resource_group()
    client = mgmt_client_factory(ComputeManagementClient)
    try:
        instances = [
            x.instance_id
            for x in client.virtual_machine_scale_set_vms.list(
                resource_group, str(name)
            )
        ]
        return instances
    except CloudError as err:
        logging.error("cloud error listing vmss: %s (%s)", name, err)

    return None


def delete_vmss(name: UUID) -> Any:
    resource_group = get_base_resource_group()
    compute_client = mgmt_client_factory(ComputeManagementClient)
    try:
        compute_client.virtual_machine_scale_sets.delete(resource_group, str(name))
    except CloudError as err:
        logging.error("cloud error deleting vmss: %s (%s)", name, err)


def get_vmss(name: UUID) -> Optional[Any]:
    resource_group = get_base_resource_group()
    logging.debug("getting vm: %s", name)
    compute_client = mgmt_client_factory(ComputeManagementClient)
    try:
        return compute_client.virtual_machine_scale_sets.get(resource_group, str(name))
    except CloudError as err:
        logging.debug("vm does not exist %s", err)
        return None


def resize_vmss(name: UUID, capacity: int) -> None:
    check_can_update(name)

    resource_group = get_base_resource_group()
    logging.info("updating VM count - name: %s vm_count: %d", name, capacity)
    compute_client = mgmt_client_factory(ComputeManagementClient)
    compute_client.virtual_machine_scale_sets.update(
        resource_group, str(name), {"sku": {"capacity": capacity}}
    )


def get_vmss_size(name: UUID) -> Optional[int]:
    vmss = get_vmss(name)
    if vmss is None:
        return None
    return cast(int, vmss.sku.capacity)


def list_instance_ids(name: UUID) -> Dict[UUID, str]:
    logging.debug("get instance IDs for scaleset: %s", name)
    resource_group = get_base_resource_group()
    compute_client = mgmt_client_factory(ComputeManagementClient)

    results = {}
    try:
        for instance in compute_client.virtual_machine_scale_set_vms.list(
            resource_group, str(name)
        ):
            results[UUID(instance.vm_id)] = cast(str, instance.instance_id)
    except CloudError:
        logging.debug("scaleset not available: %s", name)
    return results


@cached(ttl=60)
def get_instance_id(name: UUID, vm_id: UUID) -> Union[str, Error]:
    resource_group = get_base_resource_group()
    logging.info("get instance ID for scaleset node: %s:%s", name, vm_id)
    compute_client = mgmt_client_factory(ComputeManagementClient)

    vm_id_str = str(vm_id)
    for instance in compute_client.virtual_machine_scale_set_vms.list(
        resource_group, str(name)
    ):
        if instance.vm_id == vm_id_str:
            return cast(str, instance.instance_id)

    return Error(
        code=ErrorCode.UNABLE_TO_FIND,
        errors=["unable to find scaleset machine: %s:%s" % (name, vm_id)],
    )


class UnableToUpdate(Exception):
    pass


def check_can_update(name: UUID) -> Any:
    vmss = get_vmss(name)
    if vmss is None:
        raise UnableToUpdate

    if vmss.provisioning_state != "Succeeded":
        raise UnableToUpdate

    return vmss


def reimage_vmss_nodes(name: UUID, vm_ids: List[UUID]) -> Optional[Error]:
    check_can_update(name)

    resource_group = get_base_resource_group()
    logging.info("reimaging scaleset VM - name: %s vm_ids:%s", name, vm_ids)
    compute_client = mgmt_client_factory(ComputeManagementClient)

    instance_ids = []
    machine_to_id = list_instance_ids(name)
    for vm_id in vm_ids:
        if vm_id in machine_to_id:
            instance_ids.append(machine_to_id[vm_id])
        else:
            logging.info("unable to find vm_id for %s:%s", name, vm_id)

    if instance_ids:
        compute_client.virtual_machine_scale_sets.reimage_all(
            resource_group, str(name), instance_ids=instance_ids
        )
    return None


def delete_vmss_nodes(name: UUID, vm_ids: List[UUID]) -> Optional[Error]:
    check_can_update(name)

    resource_group = get_base_resource_group()
    logging.info("deleting scaleset VM - name: %s vm_ids:%s", name, vm_ids)
    compute_client = mgmt_client_factory(ComputeManagementClient)

    instance_ids = []
    machine_to_id = list_instance_ids(name)
    for vm_id in vm_ids:
        if vm_id in machine_to_id:
            instance_ids.append(machine_to_id[vm_id])
        else:
            logging.info("unable to find vm_id for %s:%s", name, vm_id)

    if instance_ids:
        compute_client.virtual_machine_scale_sets.delete_instances(
            resource_group, str(name), instance_ids=instance_ids
        )
    return None


def update_extensions(name: UUID, extensions: List[Any]) -> None:
    check_can_update(name)

    resource_group = get_base_resource_group()
    logging.info("updating VM extensions: %s", name)
    compute_client = mgmt_client_factory(ComputeManagementClient)
    compute_client.virtual_machine_scale_sets.update(
        resource_group,
        str(name),
        {"virtual_machine_profile": {"extension_profile": {"extensions": extensions}}},
    )


def create_vmss(
    location: Region,
    name: UUID,
    vm_sku: str,
    vm_count: int,
    image: str,
    network_id: str,
    spot_instances: bool,
    extensions: List[Any],
    password: str,
    ssh_public_key: str,
    tags: Dict[str, str],
) -> Optional[Error]:

    vmss = get_vmss(name)
    if vmss is not None:
        return None

    logging.info(
        "creating VM count"
        "name: %s vm_sku: %s vm_count: %d "
        "image: %s subnet: %s spot_instances: %s",
        name,
        vm_sku,
        vm_count,
        image,
        network_id,
        spot_instances,
    )

    resource_group = get_base_resource_group()

    compute_client = mgmt_client_factory(ComputeManagementClient)

    if image.startswith("/"):
        image_ref = {"id": image}
    else:
        image_val = image.split(":", 4)
        image_ref = {
            "publisher": image_val[0],
            "offer": image_val[1],
            "sku": image_val[2],
            "version": image_val[3],
        }

    sku = {"name": vm_sku, "tier": "Standard", "capacity": vm_count}

    params: Dict[str, Any] = {
        "location": location,
        "do_not_run_extensions_on_overprovisioned_vms": True,
        "upgrade_policy": {"mode": "Manual"},
        "sku": sku,
        "identity": {
            "type": "userAssigned",
            "userAssignedIdentities": {get_scaleset_identity_resource_path(): {}},
        },
        "virtual_machine_profile": {
            "priority": "Regular",
            "storage_profile": {"image_reference": image_ref},
            "os_profile": {
                "computer_name_prefix": "node",
                "admin_username": "onefuzz",
                "admin_password": password,
            },
            "network_profile": {
                "network_interface_configurations": [
                    {
                        "name": "onefuzz-nic",
                        "primary": True,
                        "ip_configurations": [
                            {"name": "onefuzz-ip-config", "subnet": {"id": network_id}}
                        ],
                    }
                ]
            },
            "extension_profile": {"extensions": extensions},
        },
        "single_placement_group": False,
    }

    image_os = get_os(location, image)
    if isinstance(image_os, Error):
        return image_os

    if image_os == OS.linux:
        params["virtual_machine_profile"]["os_profile"]["linux_configuration"] = {
            "disable_password_authentication": True,
            "ssh": {
                "public_keys": [
                    {
                        "path": "/home/onefuzz/.ssh/authorized_keys",
                        "key_data": ssh_public_key,
                    }
                ]
            },
        }

    if spot_instances:
        # Setting max price to -1 means it won't be evicted because of
        # price.
        #
        # https://docs.microsoft.com/en-us/azure/
        #   virtual-machine-scale-sets/use-spot#resource-manager-templates
        params["virtual_machine_profile"].update(
            {
                "eviction_policy": "Delete",
                "priority": "Spot",
                "billing_profile": {"max_price": -1},
            }
        )

    params["tags"] = tags.copy()
    owner = os.environ.get("ONEFUZZ_OWNER")
    if owner:
        params["tags"]["OWNER"] = owner

    try:
        compute_client.virtual_machine_scale_sets.create_or_update(
            resource_group, name, params
        )
    except CloudError as err:
        if "The request failed due to conflict with a concurrent request" in repr(err):
            logging.debug(
                "create VM had conflicts with concurrent request, ignoring %s", err
            )
            return None
        return Error(
            code=ErrorCode.VM_CREATE_FAILED,
            errors=["creating vmss: %s" % err],
        )

    return None


@cached(ttl=60)
def list_available_skus(location: str) -> List[str]:
    compute_client = mgmt_client_factory(ComputeManagementClient)
    skus: List[ResourceSku] = list(
        compute_client.resource_skus.list(filter="location eq '%s'" % location)
    )
    sku_names: List[str] = []
    for sku in skus:
        available = True
        if sku.restrictions is not None:
            for restriction in sku.restrictions:
                if restriction.type == ResourceSkuRestrictionsType.location and (
                    location.upper() in [v.upper() for v in restriction.values]
                ):
                    available = False
                    break

        if available:
            sku_names.append(sku.name)
    return sku_names
