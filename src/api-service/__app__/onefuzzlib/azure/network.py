#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from typing import Optional, Union

from msrestazure.azure_exceptions import CloudError
from onefuzztypes.enums import ErrorCode
from onefuzztypes.models import Error
from onefuzztypes.primitives import Region

from .creds import get_base_resource_group
from .subnet import create_virtual_network, delete_subnet, get_subnet_id


class Network:
    def __init__(self, region: Region):
        self.group = get_base_resource_group()
        self.region = region

    def exists(self) -> bool:
        return self.get_id() is not None

    def get_id(self) -> Optional[str]:
        return get_subnet_id(self.group, self.region)

    def create(self) -> Union[None, Error]:
        if not self.exists():
            result = create_virtual_network(self.group, self.region, self.region)
            if isinstance(result, CloudError):
                error = Error(
                    code=ErrorCode.UNABLE_TO_CREATE_NETWORK, errors=[result.message]
                )
                logging.error(
                    "network creation failed: %s- %s",
                    self.region,
                    error,
                )
                return error

        return None

    def delete(self) -> None:
        delete_subnet(self.group, self.region)
