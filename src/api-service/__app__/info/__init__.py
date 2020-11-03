#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import azure.functions as func
from onefuzztypes.responses import Info

from ..onefuzzlib.azure.creds import (
    get_base_region,
    get_base_resource_group,
    get_instance_id,
    get_subscription,
)
from ..onefuzzlib.request import ok
from ..onefuzzlib.versions import versions


def main(req: func.HttpRequest) -> func.HttpResponse:
    return ok(
        Info(
            resource_group=get_base_resource_group(),
            region=get_base_region(),
            subscription=get_subscription(),
            versions=versions(),
            instance_id=get_instance_id(),
        )
    )
