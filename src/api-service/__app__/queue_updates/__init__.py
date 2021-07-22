#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json

import azure.functions as func

from ..onefuzzlib.updates import Update, execute_update


def main(msg: func.QueueMessage) -> None:
    body = msg.get_body()
    update = Update.parse_obj(json.loads(body))
    execute_update(update)
