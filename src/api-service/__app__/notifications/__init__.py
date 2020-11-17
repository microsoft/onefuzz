#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging

import azure.functions as func
from onefuzztypes.models import Error
from onefuzztypes.requests import NotificationCreate, NotificationGet

from ..onefuzzlib.notifications.main import Notification
from ..onefuzzlib.request import not_ok, ok, parse_request


def get(req: func.HttpRequest) -> func.HttpResponse:
    entries = Notification.search()
    for entry in entries:
        entry.config.redact()

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
    entry.config.redact()
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
