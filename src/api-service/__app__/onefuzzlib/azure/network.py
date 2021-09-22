#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
import uuid
from typing import Optional, Union

from msrestazure.azure_exceptions import CloudError
from onefuzztypes.enums import ErrorCode
from onefuzztypes.models import Error
from onefuzztypes.primitives import Region

from ..config import InstanceConfig
from .creds import get_base_resource_group
from .subnet import create_virtual_network, get_subnet_id

# these were original address spaces chosen for 1f.  However, moving forwards
# these are configurable in the Instance Config.  Network names will be
# calculated from the address_space/subnet *except* if they are the original
# values.  This allows backwards compatability to existing configs if you don't
# change the network configs.
ORIGINAL_ADDRESS_SPACE = "10.0.0.0/8"
ORIGINAL_SUBNET = "10.0.0.0/16"

# This was generated randomly and should be preserved moving forwards
NETWORK_GUID_NAMESPACE = uuid.UUID("372977ad-b533-416a-b1b4-f770898e0b11")


class Network:
    def __init__(self, region: Region):
        self.group = get_base_resource_group()
        self.region = region
        self.network_config = InstanceConfig.fetch().network_config

        if (
            self.network_config.address_space == ORIGINAL_ADDRESS_SPACE
            and self.network_config.subnet == ORIGINAL_SUBNET
        ):
            self.name: str = self.region
        else:
            network_id = uuid.uuid5(
                NETWORK_GUID_NAMESPACE,
                "|".join(
                    [self.network_config.address_space, self.network_config.subnet]
                ),
            )
            self.name = f"{self.region}-{network_id}"

    def exists(self) -> bool:
        return self.get_id() is not None

    def get_id(self) -> Optional[str]:
        return get_subnet_id(self.group, self.name, self.region)

    def create(self) -> Union[None, Error]:
        if not self.exists():
            result = create_virtual_network(
                self.group, self.name, self.region, self.network_config
            )
            if isinstance(result, CloudError):
                error = Error(
                    code=ErrorCode.UNABLE_TO_CREATE_NETWORK, errors=[result.message]
                )
                logging.error(
                    "network creation failed: %s:%s- %s",
                    self.name,
                    self.region,
                    error,
                )
                return error

        return None
