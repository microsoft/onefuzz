#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json

import azure.functions as func
from ..onefuzzlib.webhooks import WebhookMessageQueueObj, WebhookMessage


def main(msg: func.QueueMessage) -> None:
    body = msg.get_body()
    obj = WebhookMessageQueueObj.parse_obj(json.loads(body))
    WebhookMessage.process_from_queue(obj)
