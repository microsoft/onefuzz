#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from typing import Union

from azure.core.exceptions import ResourceNotFoundError
from memoization import cached
from msrestazure.azure_exceptions import CloudError
from msrestazure.tools import parse_resource_id
from onefuzztypes.enums import OS, ErrorCode
from onefuzztypes.models import Error
from onefuzztypes.primitives import Region

from .compute import get_compute_client


@cached(ttl=60)
def get_os(region: Region, image: str) -> Union[Error, OS]:
    client = get_compute_client()
    parsed = parse_resource_id(image)
    if "resource_group" in parsed:
        if parsed["type"] == "galleries":
            try:
                name = client.gallery_images.get(
                    parsed["resource_group"], parsed["name"], parsed["child_name_1"]
                ).os_type.lower()
            except (ResourceNotFoundError, CloudError) as err:
                return Error(code=ErrorCode.INVALID_IMAGE, errors=[str(err)])
        else:
            try:
                name = client.images.get(
                    parsed["resource_group"], parsed["name"]
                ).storage_profile.os_disk.os_type.lower()
            except (ResourceNotFoundError, CloudError) as err:
                return Error(code=ErrorCode.INVALID_IMAGE, errors=[str(err)])
    else:
        publisher, offer, sku, version = image.split(":")
        try:
            if version == "latest":
                version = client.virtual_machine_images.list(
                    region, publisher, offer, sku, top=1
                )[0].name
            name = client.virtual_machine_images.get(
                region, publisher, offer, sku, version
            ).os_disk_image.operating_system.lower()
        except (ResourceNotFoundError, CloudError) as err:
            return Error(code=ErrorCode.INVALID_IMAGE, errors=[str(err)])
    return OS[name]
