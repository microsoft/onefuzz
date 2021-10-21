#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
import os
from typing import Dict, List, Optional, Set, Union, cast

from azure.core.exceptions import HttpResponseError, ResourceNotFoundError
from azure.mgmt.network.models import (
    NetworkInterface,
    NetworkSecurityGroup,
    SecurityRule,
    SecurityRuleAccess,
    SecurityRuleProtocol,
)
from msrestazure.azure_exceptions import CloudError
from onefuzztypes.enums import ErrorCode
from onefuzztypes.models import Error
from onefuzztypes.primitives import Region
from pydantic import BaseModel, validator

from .creds import get_base_resource_group
from .network_mgmt_client import get_network_client


def is_concurrent_request_error(err: str) -> bool:
    return "The request failed due to conflict with a concurrent request" in str(err)


def get_nsg(name: str) -> Optional[NetworkSecurityGroup]:
    resource_group = get_base_resource_group()

    logging.debug("getting nsg: %s", name)
    network_client = get_network_client()
    try:
        nsg = network_client.network_security_groups.get(resource_group, name)
        return cast(NetworkSecurityGroup, nsg)
    except (ResourceNotFoundError, CloudError) as err:
        logging.debug("nsg %s does not exist: %s", name, err)
        return None


def create_nsg(name: str, location: Region) -> Union[None, Error]:
    resource_group = get_base_resource_group()

    logging.info("creating nsg %s:%s:%s", resource_group, location, name)
    network_client = get_network_client()

    params: Dict = {
        "location": location,
    }

    if "ONEFUZZ_OWNER" in os.environ:
        params["tags"] = {"OWNER": os.environ["ONEFUZZ_OWNER"]}

    try:
        network_client.network_security_groups.begin_create_or_update(
            resource_group, name, params
        )
    except (ResourceNotFoundError, CloudError) as err:
        if is_concurrent_request_error(str(err)):
            logging.debug(
                "create NSG had conflicts with concurrent request, ignoring %s", err
            )
            return None
        return Error(
            code=ErrorCode.UNABLE_TO_CREATE,
            errors=["Unable to create nsg %s due to %s" % (name, err)],
        )
    return None


def list_nsgs() -> List[NetworkSecurityGroup]:
    resource_group = get_base_resource_group()
    network_client = get_network_client()
    return list(network_client.network_security_groups.list(resource_group))


def update_nsg(nsg: NetworkSecurityGroup) -> Union[None, Error]:
    resource_group = get_base_resource_group()

    logging.info("updating nsg %s:%s:%s", resource_group, nsg.location, nsg.name)
    network_client = get_network_client()

    try:
        network_client.network_security_groups.begin_create_or_update(
            resource_group, nsg.name, nsg
        )
    except (ResourceNotFoundError, CloudError) as err:
        if is_concurrent_request_error(str(err)):
            logging.debug(
                "create NSG had conflicts with concurrent request, ignoring %s", err
            )
            return None
        return Error(
            code=ErrorCode.UNABLE_TO_CREATE,
            errors=["Unable to update nsg %s due to %s" % (nsg.name, err)],
        )
    return None


def ok_to_delete(active_regions: Set[Region], nsg_region: str, nsg_name: str) -> bool:
    return nsg_region not in active_regions and nsg_region == nsg_name


def delete_nsg(name: str) -> bool:
    # NSG can be only deleted if no other resource is associated with it
    resource_group = get_base_resource_group()

    logging.info("deleting nsg: %s %s", resource_group, name)
    network_client = get_network_client()

    try:
        network_client.network_security_groups.begin_delete(resource_group, name)
        return True
    except HttpResponseError as err:
        err_str = str(err)
        if (
            "cannot be deleted because it is in use by the following resources"
        ) in err_str:
            return False
    except ResourceNotFoundError:
        return True

    return False


def set_allowed(name: str, sources: List[str]) -> Union[None, Error]:
    resource_group = get_base_resource_group()
    nsg = get_nsg(name)
    if not nsg:
        return Error(
            code=ErrorCode.UNABLE_TO_FIND,
            errors=["cannot update nsg rules. nsg %s not found" % name],
        )

    logging.info(
        "setting allowed incoming connection sources for nsg: %s %s",
        resource_group,
        name,
    )
    security_rules = []
    # NSG security rule priority range defined here:
    # https://docs.microsoft.com/en-us/azure/virtual-network/network-security-groups-overview
    min_priority = 100
    # NSG rules per NSG limits:
    # https://docs.microsoft.com/en-us/azure/azure-resource-manager/management/azure-subscription-service-limits?toc=/azure/virtual-network/toc.json#networking-limits
    max_rule_count = 1000
    if len(sources) > max_rule_count:
        return Error(
            code=ErrorCode.INVALID_REQUEST,
            errors=[
                "too many rules provided %d. Max allowed: %d"
                % ((len(sources)), max_rule_count),
            ],
        )

    priority = min_priority
    for src in sources:
        security_rules.append(
            SecurityRule(
                name="Allow" + str(priority),
                protocol=SecurityRuleProtocol.TCP,
                source_port_range="*",
                destination_port_range="*",
                source_address_prefix=src,
                destination_address_prefix="*",
                access=SecurityRuleAccess.ALLOW,
                priority=priority,  # between 100 and 4096
                direction="Inbound",
            )
        )
        # Will not exceed `max_rule_count` or max NSG priority (4096)
        # due to earlier check of `len(sources)`.
        priority += 1

    nsg.security_rules = security_rules
    return update_nsg(nsg)


def clear_all_rules(name: str) -> Union[None, Error]:
    return set_allowed(name, [])


def get_all_rules(name: str) -> Union[Error, List[SecurityRule]]:
    nsg = get_nsg(name)
    if not nsg:
        return Error(
            code=ErrorCode.UNABLE_TO_FIND,
            errors=["cannot get nsg rules. nsg %s not found" % name],
        )

    return cast(List[SecurityRule], nsg.security_rules)


def associate_nic(name: str, nic: NetworkInterface) -> Union[None, Error]:
    resource_group = get_base_resource_group()
    nsg = get_nsg(name)
    if not nsg:
        return Error(
            code=ErrorCode.UNABLE_TO_FIND,
            errors=["cannot associate nic. nsg %s not found" % name],
        )

    if nsg.location != nic.location:
        return Error(
            code=ErrorCode.UNABLE_TO_UPDATE,
            errors=[
                "network interface and nsg have to be in the same region.",
                "nsg %s %s, nic: %s %s"
                % (nsg.name, nsg.location, nic.name, nic.location),
            ],
        )

    if nic.network_security_group and nic.network_security_group.id == nsg.id:
        return None

    logging.info("associating nic %s with nsg: %s %s", nic.name, resource_group, name)

    nic.network_security_group = nsg
    network_client = get_network_client()
    try:
        network_client.network_interfaces.begin_create_or_update(
            resource_group, nic.name, nic
        )
    except (ResourceNotFoundError, CloudError) as err:
        if is_concurrent_request_error(str(err)):
            logging.debug(
                "associate NSG with NIC had conflicts",
                "with concurrent request, ignoring %s",
                err,
            )
            return None
        return Error(
            code=ErrorCode.UNABLE_TO_UPDATE,
            errors=[
                "Unable to associate nsg %s with nic %s due to %s"
                % (
                    name,
                    nic.name,
                    err,
                )
            ],
        )

    return None


def dissociate_nic(name: str, nic: NetworkInterface) -> Union[None, Error]:
    if nic.network_security_group is None:
        return None
    resource_group = get_base_resource_group()
    nsg = get_nsg(name)
    if not nsg:
        return Error(
            code=ErrorCode.UNABLE_TO_FIND,
            errors=["cannot update nsg rules. nsg %s not found" % name],
        )
    if nsg.id != nic.network_security_group.id:
        return Error(
            code=ErrorCode.UNABLE_TO_UPDATE,
            errors=[
                "network interface is not associated with this nsg.",
                "nsg %s, nic: %s, nic.nsg: %s"
                % (
                    nsg.id,
                    nic.name,
                    nic.network_security_group.id,
                ),
            ],
        )

    logging.info("dissociating nic %s with nsg: %s %s", nic.name, resource_group, name)

    nic.network_security_group = None
    network_client = get_network_client()
    try:
        network_client.network_interfaces.begin_create_or_update(
            resource_group, nic.name, nic
        )
    except (ResourceNotFoundError, CloudError) as err:
        if is_concurrent_request_error(str(err)):
            logging.debug(
                "associate NSG with NIC had conflicts with ",
                "concurrent request, ignoring %s",
                err,
            )
            return None
        return Error(
            code=ErrorCode.UNABLE_TO_UPDATE,
            errors=[
                "Unable to associate nsg %s with nic %s due to %s"
                % (
                    name,
                    nic.name,
                    err,
                )
            ],
        )

    return None


class NSG(BaseModel):
    name: str
    region: Region

    @validator("name", allow_reuse=True)
    def check_name(cls, value: str) -> str:
        # https://docs.microsoft.com/en-us/azure/azure-resource-manager/management/resource-name-rules
        if len(value) > 80:
            raise ValueError("NSG name too long")
        return value

    def create(self) -> Union[None, Error]:
        # Optimization: if NSG exists - do not try
        # to create it
        if self.get() is not None:
            return None

        return create_nsg(self.name, self.region)

    def delete(self) -> bool:
        return delete_nsg(self.name)

    def get(self) -> Optional[NetworkSecurityGroup]:
        return get_nsg(self.name)

    def set_allowed_sources(self, sources: List[str]) -> Union[None, Error]:
        return set_allowed(self.name, sources)

    def clear_all_rules(self) -> Union[None, Error]:
        return clear_all_rules(self.name)

    def get_all_rules(self) -> Union[Error, List[SecurityRule]]:
        return get_all_rules(self.name)

    def associate_nic(self, nic: NetworkInterface) -> Union[None, Error]:
        return associate_nic(self.name, nic)

    def dissociate_nic(self, nic: NetworkInterface) -> Union[None, Error]:
        return dissociate_nic(self.name, nic)
