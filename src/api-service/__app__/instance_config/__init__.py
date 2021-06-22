#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import azure.functions as func
from onefuzztypes.enums import ErrorCode
from onefuzztypes.models import Error
from onefuzztypes.requests import InstanceConfigUpdate

from ..onefuzzlib.config import InstanceConfig
from ..onefuzzlib.endpoint_authorization import call_if_user
from ..onefuzzlib.events import get_events
from ..onefuzzlib.request import not_ok, ok, parse_request
from ..onefuzzlib.user_credentials import parse_jwt_token


def get(req: func.HttpRequest) -> func.HttpResponse:
    return ok(InstanceConfig.fetch())


def post(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(InstanceConfigUpdate, req)
    if isinstance(request, Error):
        return not_ok(request, context="instance_config_update")

    config = InstanceConfig.fetch()

    user_info = parse_jwt_token(req)
    if isinstance(user_info, Error):
        return not_ok(user_info, context="missing user info")

    if config.admins:
        if not user_info.object_id:
            return not_ok(
                Error(
                    code=ErrorCode.INVALID_PERMISSION,
                    errors=["unauthorized (missing object_id)"],
                ),
                context="instance_config_update",
            )

        if user_info.object_id not in config.admins:
            return not_ok(
                Error(code=ErrorCode.INVALID_PERMISSION, errors=["unauthorized"]),
                context="instance_config_update",
            )

    for field in config.__fields__:
        if hasattr(request.config, field):
            setattr(config, field, getattr(request.config, field))

    config.save()
    return ok(config)


def main(req: func.HttpRequest, dashboard: func.Out[str]) -> func.HttpResponse:
    methods = {"GET": get, "POST": post}
    method = methods[req.method]
    result = call_if_user(req, method)

    events = get_events()
    if events:
        dashboard.set(events)

    return result
