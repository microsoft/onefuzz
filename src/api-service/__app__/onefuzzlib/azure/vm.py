#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
import os
from typing import Any, Dict, List, Optional, Union, cast
from uuid import UUID

from azure.core.exceptions import ResourceNotFoundError
from azure.mgmt.compute.models import VirtualMachine
from msrestazure.azure_exceptions import CloudError
from onefuzztypes.enums import OS, ErrorCode
from onefuzztypes.models import Authentication, Error
from onefuzztypes.primitives import Extension, Region
from pydantic import BaseModel, validator

from .compute import get_compute_client
from .creds import get_base_resource_group
from .disk import delete_disk, list_disks
from .image import get_os
from .ip import create_public_nic, delete_ip, delete_nic, get_ip, get_public_nic
from .nsg import NSG


def get_vm(name: str) -> Optional[VirtualMachine]:
    resource_group = get_base_resource_group()

    logging.debug("getting vm: %s", name)
    compute_client = get_compute_client()
    try:
        return cast(
            VirtualMachine,
            compute_client.virtual_machines.get(
                resource_group, name, expand="instanceView"
            ),
        )
    except (ResourceNotFoundError, CloudError) as err:
        logging.debug("vm does not exist %s", err)
        return None


def create_vm(
    name: str,
    location: Region,
    vm_sku: str,
    image: str,
    password: str,
    ssh_public_key: str,
    nsg: Optional[NSG],
) -> Union[None, Error]:
    resource_group = get_base_resource_group()
    logging.info("creating vm %s:%s:%s", resource_group, location, name)

    compute_client = get_compute_client()

    nic = get_public_nic(resource_group, name)
    if nic is None:
        result = create_public_nic(resource_group, name, location)
        if isinstance(result, Error):
            return result
        logging.info("waiting on nic creation")
        return None
    if nsg:
        result = nsg.associate_nic(nic)
        if isinstance(result, Error):
            return result

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

    params: Dict = {
        "location": location,
        "os_profile": {
            "computer_name": "node",
            "admin_username": "onefuzz",
        },
        "hardware_profile": {"vm_size": vm_sku},
        "storage_profile": {"image_reference": image_ref},
        "network_profile": {"network_interfaces": [{"id": nic.id}]},
    }

    image_os = get_os(location, image)
    if isinstance(image_os, Error):
        return image_os

    if image_os == OS.windows:
        params["os_profile"]["admin_password"] = password

    if image_os == OS.linux:

        params["os_profile"]["linux_configuration"] = {
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

    if "ONEFUZZ_OWNER" in os.environ:
        params["tags"] = {"OWNER": os.environ["ONEFUZZ_OWNER"]}

    try:
        compute_client.virtual_machines.begin_create_or_update(
            resource_group, name, params
        )
    except (ResourceNotFoundError, CloudError) as err:
        if "The request failed due to conflict with a concurrent request" in str(err):
            logging.debug(
                "create VM had conflicts with concurrent request, ignoring %s", err
            )
            return None
        return Error(code=ErrorCode.VM_CREATE_FAILED, errors=[str(err)])
    return None


def get_extension(vm_name: str, extension_name: str) -> Optional[Any]:
    resource_group = get_base_resource_group()

    logging.debug(
        "getting extension: %s:%s:%s",
        resource_group,
        vm_name,
        extension_name,
    )
    compute_client = get_compute_client()
    try:
        return compute_client.virtual_machine_extensions.get(
            resource_group, vm_name, extension_name
        )
    except (ResourceNotFoundError, CloudError) as err:
        logging.info("extension does not exist %s", err)
        return None


def create_extension(vm_name: str, extension: Dict) -> Any:
    resource_group = get_base_resource_group()

    logging.info(
        "creating extension: %s:%s:%s", resource_group, vm_name, extension["name"]
    )
    compute_client = get_compute_client()
    return compute_client.virtual_machine_extensions.begin_create_or_update(
        resource_group, vm_name, extension["name"], extension
    )


def delete_vm(name: str) -> Any:
    resource_group = get_base_resource_group()

    logging.info("deleting vm: %s %s", resource_group, name)
    compute_client = get_compute_client()
    return compute_client.virtual_machines.begin_delete(resource_group, name)


def has_components(name: str) -> bool:
    # check if any of the components associated with a VM still exist.
    #
    # Azure VM Deletion requires we first delete the VM, then delete all of it's
    # resources.  This is required to ensure we've cleaned it all up before
    # marking it "done"
    resource_group = get_base_resource_group()
    if get_vm(name):
        return True
    if get_public_nic(resource_group, name):
        return True
    if get_ip(resource_group, name):
        return True

    disks = [x.name for x in list_disks(resource_group) if x.name.startswith(name)]
    if disks:
        return True

    return False


def delete_vm_components(name: str, nsg: Optional[NSG]) -> bool:
    resource_group = get_base_resource_group()
    logging.info("deleting vm components %s:%s", resource_group, name)
    if get_vm(name):
        logging.info("deleting vm %s:%s", resource_group, name)
        delete_vm(name)
        return False

    nic = get_public_nic(resource_group, name)
    if nic:
        logging.info("deleting nic %s:%s", resource_group, name)
        if nic.network_security_group and nsg:
            nsg.dissociate_nic(nic)
            return False
        delete_nic(resource_group, name)
        return False

    if get_ip(resource_group, name):
        logging.info("deleting ip %s:%s", resource_group, name)
        delete_ip(resource_group, name)
        return False

    disks = [x.name for x in list_disks(resource_group) if x.name.startswith(name)]
    if disks:
        for disk in disks:
            logging.info("deleting disk %s:%s", resource_group, disk)
            delete_disk(resource_group, disk)
        return False

    return True


class VM(BaseModel):
    name: Union[UUID, str]
    region: Region
    sku: str
    image: str
    auth: Authentication
    nsg: Optional[NSG]

    @validator("name", allow_reuse=True)
    def check_name(cls, value: Union[UUID, str]) -> Union[UUID, str]:
        if isinstance(value, str):
            if len(value) > 40:
                # Azure truncates resources if the names are longer than 40
                # bytes
                raise ValueError("VM name too long")
        return value

    def is_deleted(self) -> bool:
        # A VM is considered deleted once all of it's resources including disks,
        # NICs, IPs, as well as the VM are deleted
        return not has_components(str(self.name))

    def exists(self) -> bool:
        return self.get() is not None

    def get(self) -> Optional[VirtualMachine]:
        return get_vm(str(self.name))

    def create(self) -> Union[None, Error]:
        if self.get() is not None:
            return None

        logging.info("vm creating: %s", self.name)
        return create_vm(
            str(self.name),
            self.region,
            self.sku,
            self.image,
            self.auth.password,
            self.auth.public_key,
            self.nsg,
        )

    def delete(self) -> bool:
        return delete_vm_components(str(self.name), self.nsg)

    def add_extensions(self, extensions: List[Extension]) -> Union[bool, Error]:
        status = []
        to_create = []
        for config in extensions:
            if not isinstance(config["name"], str):
                logging.error("vm agent  - incompatable name: %s", repr(config))
                continue
            extension = get_extension(str(self.name), config["name"])

            if extension:
                logging.info(
                    "vm extension state: %s - %s - %s",
                    self.name,
                    config["name"],
                    extension.provisioning_state,
                )
                status.append(extension.provisioning_state)
            else:
                to_create.append(config)

        if to_create:
            for config in to_create:
                create_extension(str(self.name), config)
        else:
            if all([x == "Succeeded" for x in status]):
                return True
            elif "Failed" in status:
                return Error(
                    code=ErrorCode.VM_CREATE_FAILED,
                    errors=["failed to launch extension"],
                )
            elif not ("Creating" in status or "Updating" in status):
                logging.error("vm agent - unknown state %s: %s", self.name, status)

        return False
