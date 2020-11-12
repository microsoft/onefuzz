#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging

import azure.functions as func
from onefuzztypes.models import Error
from onefuzztypes.requests import WebhookGet

from ..onefuzzlib.request import not_ok, ok, parse_request
from ..onefuzzlib.webhooks import Webhook, WebhookMessageLog


def post(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(WebhookGet, req)
    if isinstance(request, Error):
        return not_ok(request, context="webhook log")

    webhook = Webhook.get_by_id(request.webhook_id)
    if isinstance(webhook, Error):
        return not_ok(webhook, context="webhook log")

    logging.info("getting webhook logs: %s", request.webhook_id)
    logs = WebhookMessageLog.search(query={"webhook_id": [request.webhook_id]})
    return ok(logs)


def main(req: func.HttpRequest) -> func.HttpResponse:
    if req.method == "POST":
        return post(req)
    else:
        raise Exception("invalid method")
