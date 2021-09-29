#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
import os

import azure.functions as func
from onefuzztypes.enums import ErrorCode
from onefuzztypes.models import AgentConfig, Error
from onefuzztypes.requests import PoolCreate, PoolSearch, PoolStop
from onefuzztypes.responses import BoolResult

from ..onefuzzlib.azure.creds import (
    get_base_region,
    get_instance_id,
    get_instance_url,
    get_regions,
)
from ..onefuzzlib.azure.queue import get_queue_sas
from ..onefuzzlib.azure.storage import StorageType
from ..onefuzzlib.azure.vmss import list_available_skus
from ..onefuzzlib.endpoint_authorization import call_if_user, check_can_manage_pools
from ..onefuzzlib.request import not_ok, ok, parse_request
from ..onefuzzlib.workers.pools import Pool


def set_config(pool: Pool) -> Pool:
    pool.config = AgentConfig(
        pool_name=pool.name,
        onefuzz_url=get_instance_url(),
        instance_telemetry_key=os.environ.get("APPINSIGHTS_INSTRUMENTATIONKEY"),
        microsoft_telemetry_key=os.environ.get("ONEFUZZ_TELEMETRY"),
        heartbeat_queue=get_queue_sas(
            "node-heartbeat",
            StorageType.config,
            add=True,
        ),
        instance_id=get_instance_id(),
    )

    multi_tenant_domain = os.environ.get("MULTI_TENANT_DOMAIN")
    if multi_tenant_domain:
        pool.config.multi_tenant_domain = multi_tenant_domain

    return pool


def get(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(PoolSearch, req)
    if isinstance(request, Error):
        return not_ok(request, context="pool get")

    if request.name:
        pool = Pool.get_by_name(request.name)
        if isinstance(pool, Error):
            return not_ok(pool, context=request.name)
        pool.populate_scaleset_summary()
        pool.populate_work_queue()
        return ok(set_config(pool))

    if request.pool_id:
        pool = Pool.get_by_id(request.pool_id)
        if isinstance(pool, Error):
            return not_ok(pool, context=request.pool_id)
        pool.populate_scaleset_summary()
        pool.populate_work_queue()
        return ok(set_config(pool))

    pools = Pool.search_states(states=request.state)
    return ok(pools)


def post(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(PoolCreate, req)
    if isinstance(request, Error):
        return not_ok(request, context="PoolCreate")

    answer = check_can_manage_pools(req)
    if isinstance(answer, Error):
        return not_ok(answer, context="PoolCreate")

    pool = Pool.get_by_name(request.name)
    if isinstance(pool, Pool):
        return not_ok(
            Error(
                code=ErrorCode.INVALID_REQUEST,
                errors=["pool with that name already exists"],
            ),
            context=repr(request),
        )

    logging.info(request)

    if request.autoscale:
        if request.autoscale.region is None:
            request.autoscale.region = get_base_region()
        else:
            if request.autoscale.region not in get_regions():
                return not_ok(
                    Error(code=ErrorCode.UNABLE_TO_CREATE, errors=["invalid region"]),
                    context="poolcreate",
                )

        region = request.autoscale.region

        if request.autoscale.vm_sku not in list_available_skus(region):
            return not_ok(
                Error(
                    code=ErrorCode.UNABLE_TO_CREATE,
                    errors=[
                        "vm_sku '%s' is not available in the location '%s'"
                        % (request.autoscale.vm_sku, region)
                    ],
                ),
                context="poolcreate",
            )

    pool = Pool.create(
        name=request.name,
        os=request.os,
        arch=request.arch,
        managed=request.managed,
        client_id=request.client_id,
        autoscale=request.autoscale,
    )
    return ok(set_config(pool))


def delete(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(PoolStop, req)
    if isinstance(request, Error):
        return not_ok(request, context="PoolDelete")

    answer = check_can_manage_pools(req)
    if isinstance(answer, Error):
        return not_ok(answer, context="PoolDelete")

    pool = Pool.get_by_name(request.name)
    if isinstance(pool, Error):
        return not_ok(pool, context="pool stop")
    pool.set_shutdown(now=request.now)
    return ok(BoolResult(result=True))


def main(req: func.HttpRequest) -> func.HttpResponse:
    methods = {"GET": get, "POST": post, "DELETE": delete}
    method = methods[req.method]
    result = call_if_user(req, method)

    return result
