#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import azure.functions as func
from azure.core.exceptions import HttpResponseError
from onefuzztypes.enums import ErrorCode
from onefuzztypes.models import Error
from onefuzztypes.requests import InstanceConfigUpdate

from ..onefuzzlib.azure.nsg import set_allowed
from ..onefuzzlib.config import InstanceConfig
from ..onefuzzlib.endpoint_authorization import call_if_user, can_modify_config
from ..onefuzzlib.request import not_ok, ok, parse_request
from ..onefuzzlib.workers.scalesets import Scaleset


def get(req: func.HttpRequest) -> func.HttpResponse:
    return ok(InstanceConfig.fetch())


def post(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(InstanceConfigUpdate, req)
    if isinstance(request, Error):
        return not_ok(request, context="instance_config_update")

    config = InstanceConfig.fetch()

    if not can_modify_config(req, config):
        return not_ok(
            Error(code=ErrorCode.INVALID_PERMISSION, errors=["unauthorized"]),
            context="instance_config_update",
        )

    update_nsg = False
    if request.config.proxy_nsg_config and config.proxy_nsg_config:
        request_config = request.config.proxy_nsg_config
        current_config = config.proxy_nsg_config
        if set(request_config.allowed_service_tags) != set(
            current_config.allowed_service_tags
        ) or set(request_config.allowed_ips) != set(current_config.allowed_ips):
            update_nsg = True

    config.update(request.config)
    config.save()

    # Update All NSGs
    if update_nsg:
        scalesets = Scaleset.search()
        regions = set(x.region for x in scalesets)
        for region in regions:
            # nsg = get_nsg(region)
            result = set_allowed(region, request.config.proxy_nsg_config)
            if isinstance(result, Error):
                return not_ok(
                    Error(
                        code=ErrorCode.UNABLE_TO_CREATE,
                        errors=["Unable to update nsg %s due to %s" % (region, result)],
                    ),
                    context="instance_config_update",
                )

    return ok(config)


def main(req: func.HttpRequest) -> func.HttpResponse:
    methods = {"GET": get, "POST": post}
    method = methods[req.method]
    result = call_if_user(req, method)

    return result
