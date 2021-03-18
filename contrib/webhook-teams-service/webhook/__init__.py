#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.
#


import hmac
import json
import logging
import os
from hashlib import sha512
from typing import Any, Dict

import aiohttp
import azure.functions as func


def code_block(data: str) -> str:
    data = data.replace("`", "``")
    return "\n```\n%s\n```\n" % data


async def send_message(req: func.HttpRequest) -> bool:
    data = req.get_json()
    teams_url = os.environ.get("TEAMS_URL")
    if teams_url is None:
        raise Exception("missing TEAMS_URL")

    message: Dict[str, Any] = {
        "@type": "MessageCard",
        "@context": "https://schema.org/extensions",
        "summary": data["instance_name"],
        "sections": [
            {
                "facts": [
                    {"name": "instance", "value": data["instance_name"]},
                    {"name": "event type", "value": data["event_type"]},
                ]
            },
            {"text": code_block(json.dumps(data["event"], sort_keys=True))},
        ],
    }
    async with aiohttp.ClientSession() as client:
        async with client.post(teams_url, json=message) as response:
            return response.ok


def verify(req: func.HttpRequest) -> bool:
    request_hmac = req.headers.get("X-Onefuzz-Digest")
    if request_hmac is None:
        raise Exception("missing X-Onefuzz-Digest")

    hmac_token = os.environ.get("HMAC_TOKEN")
    if hmac_token is None:
        raise Exception("missing HMAC_TOKEN")

    digest = hmac.new(
        hmac_token.encode(), msg=req.get_body(), digestmod=sha512
    ).hexdigest()
    if digest != request_hmac:
        logging.error("invalid hmac")
        return False

    return True


async def main(req: func.HttpRequest) -> func.HttpResponse:
    if not verify(req):
        return func.HttpResponse("no thanks")

    if await send_message(req):
        return func.HttpResponse("unable to send message")

    return func.HttpResponse("thanks")
