#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import azure.functions as func
from onefuzztypes.enums import ErrorCode, ScalesetState
from onefuzztypes.models import Error
from onefuzztypes.requests import (
    ScalesetCreate,
    ScalesetSearch,
    ScalesetStop,
    ScalesetUpdate,
)
from onefuzztypes.responses import BoolResult

from ..onefuzzlib.azure.creds import get_base_region, get_regions
from ..onefuzzlib.azure.vmss import list_available_skus
from ..onefuzzlib.endpoint_authorization import call_if_user
from ..onefuzzlib.events import get_events
from ..onefuzzlib.request import not_ok, ok, parse_request
from ..onefuzzlib.workers.pools import Pool
from ..onefuzzlib.workers.scalesets import Scaleset


def get(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(ScalesetSearch, req)
    if isinstance(request, Error):
        return not_ok(request, context="ScalesetSearch")
    if request.scaleset_id:
        scaleset = Scaleset.get_by_id(request.scaleset_id)
        if isinstance(scaleset, Error):
            return not_ok(scaleset, context="ScalesetSearch")
        scaleset.update_nodes()
        if not request.include_auth:
            scaleset.auth = None
        return ok(scaleset)

    scalesets = Scaleset.search_states(states=request.state)
    for scaleset in scalesets:
        # don't return auths during list actions, only 'get'
        scaleset.auth = None
    return ok(scalesets)


def post(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(ScalesetCreate, req)
    if isinstance(request, Error):
        return not_ok(request, context="ScalesetCreate")

    # Verify the pool exists
    pool = Pool.get_by_name(request.pool_name)
    if isinstance(pool, Error):
        return not_ok(pool, context=repr(request))

    if not pool.managed:
        return not_ok(
            Error(
                code=ErrorCode.UNABLE_TO_CREATE,
                errors=["scalesets can only be added to managed pools"],
            ),
            context="scalesetcreate",
        )

    if request.region is None:
        region = get_base_region()
    else:
        if request.region not in get_regions():
            return not_ok(
                Error(code=ErrorCode.UNABLE_TO_CREATE, errors=["invalid region"]),
                context="scalesetcreate",
            )

        region = request.region

    if request.vm_sku not in list_available_skus(region):
        return not_ok(
            Error(
                code=ErrorCode.UNABLE_TO_CREATE,
                errors=[
                    "The specified vm_sku '%s' is not available in the location '%s'"
                    % (request.vm_sku, region)
                ],
            ),
            context="scalesetcreate",
        )

    scaleset = Scaleset.create(
        pool_name=request.pool_name,
        vm_sku=request.vm_sku,
        image=request.image,
        region=region,
        size=request.size,
        spot_instances=request.spot_instances,
        ephemeral_os_disks=request.ephemeral_os_disks,
        tags=request.tags,
    )
    # don't return auths during create, only 'get' with include_auth
    scaleset.auth = None
    return ok(scaleset)


def delete(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(ScalesetStop, req)
    if isinstance(request, Error):
        return not_ok(request, context="ScalesetDelete")

    scaleset = Scaleset.get_by_id(request.scaleset_id)
    if isinstance(scaleset, Error):
        return not_ok(scaleset, context="scaleset stop")

    if request.now:
        scaleset.state = ScalesetState.halt
    else:
        scaleset.state = ScalesetState.shutdown

    scaleset.save()
    scaleset.auth = None
    return ok(BoolResult(result=True))


def patch(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(ScalesetUpdate, req)
    if isinstance(request, Error):
        return not_ok(request, context="ScalesetUpdate")

    scaleset = Scaleset.get_by_id(request.scaleset_id)
    if isinstance(scaleset, Error):
        return not_ok(scaleset, context="ScalesetUpdate")

    if scaleset.state != ScalesetState.running:
        return not_ok(
            Error(
                code=ErrorCode.INVALID_REQUEST,
                errors=["scaleset state must be 'running' state to modify scaleset"],
            ),
            context="ScalesetUpdate",
        )

    if request.size is not None:
        scaleset.size = request.size
        scaleset.state = ScalesetState.resize

    scaleset.save()
    scaleset.auth = None
    return ok(scaleset)


def main(req: func.HttpRequest, dashboard: func.Out[str]) -> func.HttpResponse:
    methods = {"GET": get, "POST": post, "DELETE": delete, "PATCH": patch}
    method = methods[req.method]
    result = call_if_user(req, method)

    events = get_events()
    if events:
        dashboard.set(events)

    return result
