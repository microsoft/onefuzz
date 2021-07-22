#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import azure.functions as func
from onefuzztypes.responses import Info

from ..onefuzzlib.azure.creds import (
    get_base_region,
    get_base_resource_group,
    get_insights_appid,
    get_insights_instrumentation_key,
    get_instance_id,
    get_subscription,
)
from ..onefuzzlib.request import ok
from ..onefuzzlib.versions import versions


def main(req: func.HttpRequest) -> func.HttpResponse:
    response = ok(
        Info(
            resource_group=get_base_resource_group(),
            region=get_base_region(),
            subscription=get_subscription(),
            versions=versions(),
            instance_id=get_instance_id(),
            insights_appid=get_insights_appid(),
            insights_instrumentation_key=get_insights_instrumentation_key(),
        )
    )

    return response
