#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import azure.functions as func

# This endpoint handles the signalr negotation
# As we do not differentiate from clients at this time, we pass the Functions runtime
# provided connection straight to the client
#
# For more info:
# https://docs.microsoft.com/en-us/azure/azure-signalr/signalr-concept-internals



def post(req: func.HttpRequest, connectionInfoJson: str) -> func.HttpResponse:
    return func.HttpResponse(
        connectionInfoJson,
        status_code=200,
        headers={"Content-type": "application/json"},
    )


def main(req: func.HttpRequest) -> func.HttpResponse:
    methods = {"POST": post}
    method = methods[req.method]
    result = call_if_user(req, method)

    return result
