#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from typing import Optional

import azure.functions as func
from onefuzztypes.enums import ErrorCode, VmState
from onefuzztypes.models import Error
from onefuzztypes.requests import ProxyCreate, ProxyDelete, ProxyGet, ProxyReset
from onefuzztypes.responses import BoolResult, ProxyGetResult

from ..onefuzzlib.endpoint_authorization import call_if_user
from ..onefuzzlib.events import get_events
from ..onefuzzlib.proxy import Proxy
from ..onefuzzlib.proxy_forward import ProxyForward
from ..onefuzzlib.request import not_ok, ok, parse_request
from ..onefuzzlib.workers.scalesets import Scaleset


def get_result(proxy_forward: ProxyForward, proxy: Optional[Proxy]) -> ProxyGetResult:
    forward = proxy_forward.to_forward()
    if (
        proxy is None
        or proxy.state not in [VmState.running, VmState.extensions_launch]
        or proxy.heartbeat is None
        or forward not in proxy.heartbeat.forwards
    ):
        return ProxyGetResult(forward=forward)
    return ProxyGetResult(ip=proxy.ip, forward=forward)


def get(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(ProxyGet, req)
    if isinstance(request, Error):
        return not_ok(request, context="ProxyGet")

    scaleset = Scaleset.get_by_id(request.scaleset_id)
    if isinstance(scaleset, Error):
        return not_ok(scaleset, context="ProxyGet")

    proxy = Proxy.get_or_create(scaleset.region)
    forwards = ProxyForward.search_forward(
        scaleset_id=request.scaleset_id,
        machine_id=request.machine_id,
        dst_port=request.dst_port,
    )
    if not forwards:
        return not_ok(
            Error(
                code=ErrorCode.INVALID_REQUEST,
                errors=["no forwards for scaleset and node"],
            ),
            context="debug_proxy get",
        )

    return ok(get_result(forwards[0], proxy))


def post(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(ProxyCreate, req)
    if isinstance(request, Error):
        return not_ok(request, context="ProxyCreate")

    scaleset = Scaleset.get_by_id(request.scaleset_id)
    if isinstance(scaleset, Error):
        return not_ok(scaleset, context="debug_proxy create")

    forward = ProxyForward.update_or_create(
        region=scaleset.region,
        scaleset_id=scaleset.scaleset_id,
        machine_id=request.machine_id,
        # proxy_id=proxy.proxy_id,
        dst_port=request.dst_port,
        duration=request.duration,
    )
    if isinstance(forward, Error):
        return not_ok(forward, context="debug_proxy create")

    proxy = Proxy.get_or_create(scaleset.region)
    if proxy:
        proxy.save_proxy_config()
    return ok(get_result(forward, proxy))


def patch(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(ProxyReset, req)
    if isinstance(request, Error):
        return not_ok(request, context="ProxyReset")

    proxy = Proxy.get(request.region)
    if proxy is not None:
        proxy.state = VmState.stopping
        proxy.save()
        return ok(BoolResult(result=True))

    return ok(BoolResult(result=False))


def delete(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(ProxyDelete, req)
    if isinstance(request, Error):
        return not_ok(request, context="debug_proxy delete")

    regions = ProxyForward.remove_forward(
        scaleset_id=request.scaleset_id,
        machine_id=request.machine_id,
        dst_port=request.dst_port,
    )
    for region in regions:
        proxy = Proxy.get_or_create(region)
        if proxy:
            proxy.save_proxy_config()

    return ok(BoolResult(result=True))


def main(req: func.HttpRequest, dashboard: func.Out[str]) -> func.HttpResponse:
    methods = {"GET": get, "POST": post, "DELETE": delete, "PATCH": patch}
    method = methods[req.method]
    result = call_if_user(req, method)

    events = get_events()
    if events:
        dashboard.set(events)

    return result
