#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.


import azure.functions as func


def main(msg: func.QueueMessage, dashboard: func.Out[str]) -> None:
    body = msg.get_body().decode()
    dashboard.set(body)
