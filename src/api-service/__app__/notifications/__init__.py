#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging

import azure.functions as func
from onefuzztypes.models import Error
from onefuzztypes.requests import NotificationCreate, NotificationGet

from ..onefuzzlib.endpoint_authorization import call_if_user
from ..onefuzzlib.events import get_events
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

    entry = Notification.create(container=request.container, config=request.config)
    if isinstance(entry, Error):
        return not_ok(entry, context="notification create")

    return ok(entry)


def delete(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(NotificationGet, req)
    if isinstance(request, Error):
        return not_ok(request, context="notification delete")

    entry = Notification.get_by_id(request.notification_id)
    if isinstance(entry, Error):
        return not_ok(entry, context="notification delete")

    entry.delete()
    return ok(entry)


def main(req: func.HttpRequest, dashboard: func.Out[str]) -> func.HttpResponse:
    methods = {"GET": get, "POST": post, "DELETE": delete}
    method = methods[req.method]
    result = call_if_user(req, method)

    events = get_events()
    if events:
        dashboard.set(events)

    return result
