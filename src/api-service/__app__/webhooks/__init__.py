#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging

import azure.functions as func
from onefuzztypes.models import Error
from onefuzztypes.requests import (
    WebhookCreate,
    WebhookGet,
    WebhookSearch,
    WebhookUpdate,
)
from onefuzztypes.responses import BoolResult

from ..onefuzzlib.endpoint_authorization import call_if_user
from ..onefuzzlib.request import not_ok, ok, parse_request
from ..onefuzzlib.webhooks import Webhook


def get(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(WebhookSearch, req)
    if isinstance(request, Error):
        return not_ok(request, context="webhook get")

    if request.webhook_id:
        logging.info("getting webhook: %s", request.webhook_id)
        webhook = Webhook.get_by_id(request.webhook_id)
        if isinstance(webhook, Error):
            return not_ok(webhook, context="webhook update")
        webhook.url = None
        return ok(webhook)

    logging.info("listing webhooks")
    webhooks = Webhook.search()
    for webhook in webhooks:
        webhook.url = None
        webhook.secret_token = None
    return ok(webhooks)


def post(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(WebhookCreate, req)
    if isinstance(request, Error):
        return not_ok(request, context="webhook create")
    webhook = Webhook(
        name=request.name,
        url=request.url,
        event_types=request.event_types,
        secret_token=request.secret_token,
    )
    webhook.save()

    webhook.url = None
    webhook.secret_token = None

    logging.info("added webhook: %s", request)
    return ok(webhook)


def patch(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(WebhookUpdate, req)
    if isinstance(request, Error):
        return not_ok(request, context="webhook update")

    logging.info("updating webhook: %s", request.webhook_id)

    webhook = Webhook.get_by_id(request.webhook_id)
    if isinstance(webhook, Error):
        return not_ok(webhook, context="webhook update")

    if request.url is not None:
        webhook.url = request.url

    if request.name is not None:
        webhook.name = request.name

    if request.event_types is not None:
        webhook.event_types = request.event_types

    if request.secret_token is not None:
        webhook.secret_token = request.secret_token

    webhook.save()
    webhook.url = None
    webhook.secret_token = None

    return ok(webhook)


def delete(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(WebhookGet, req)
    if isinstance(request, Error):
        return not_ok(request, context="webhook delete")

    logging.info("deleting webhook: %s", request.webhook_id)

    entry = Webhook.get_by_id(request.webhook_id)
    if isinstance(entry, Error):
        return not_ok(entry, context="webhook delete")

    entry.delete()
    return ok(BoolResult(result=True))


def main(req: func.HttpRequest) -> func.HttpResponse:
    methods = {"GET": get, "POST": post, "DELETE": delete, "PATCH": patch}
    method = methods[req.method]
    result = call_if_user(req, method)

    return result
