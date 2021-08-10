#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import azure.functions as func

from ..onefuzzlib.endpoint_authorization import call_if_user

# This endpoint handles the signalr negotation
# As we do not differentiate from clients at this time, we pass the Functions runtime
# provided connection straight to the client
#
# For more info:
# https://docs.microsoft.com/en-us/azure/azure-signalr/signalr-concept-internals


def main(req: func.HttpRequest, connectionInfoJson: str) -> func.HttpResponse:
    # NOTE: this is a sub-method because the call_if* do not support callbacks with
    # additional arguments at this time.  Once call_if* supports additional arguments,
    # this should be made a generic function
    def post(req: func.HttpRequest) -> func.HttpResponse:
        return func.HttpResponse(
            connectionInfoJson,
            status_code=200,
            headers={"Content-type": "application/json"},
        )

    methods = {"POST": post}
    method = methods[req.method]

    result = call_if_user(req, method)

    return result
