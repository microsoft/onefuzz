#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging

import azure.functions as func
from onefuzztypes.enums import ErrorCode
from onefuzztypes.models import Error
from onefuzztypes.requests import NotificationCreate, NotificationGet

from ..onefuzzlib.azure.containers import get_containers
from ..onefuzzlib.notifications.main import Notification
from ..onefuzzlib.request import not_ok, ok, parse_request


def get(req: func.HttpRequest) -> func.HttpResponse:
    entries = Notification.search()
    return ok(entries)


def post(req: func.HttpRequest) -> func.HttpResponse:
    logging.info("adding notification hook")
    request = parse_request(NotificationCreate, req)
    if isinstance(request, Error):
        return not_ok(request, context="notification create")

    containers = get_containers()
    if request.container not in containers:
        return not_ok(
            Error(code=ErrorCode.INVALID_REQUEST, errors=["invalid container"]),
            context=request.container,
        )

    existing = Notification.get_existing(request.container, request.config)
    if existing is not None:
        return ok(existing)

    item = Notification(container=request.container, config=request.config)
    item.save()
    return ok(item)


def delete(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(NotificationGet, req)
    if isinstance(request, Error):
        return not_ok(request, context="notification delete")

    entry = Notification.get_by_id(request.notification_id)
    if isinstance(entry, Error):
        return not_ok(entry, context="notification delete")

    entry.delete()
    return ok(entry)


def main(req: func.HttpRequest) -> func.HttpResponse:
    if req.method == "GET":
        return get(req)
    elif req.method == "POST":
        return post(req)
    elif req.method == "DELETE":
        return delete(req)
    else:
        raise Exception("invalid method")
