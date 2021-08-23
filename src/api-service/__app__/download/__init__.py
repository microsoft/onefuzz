#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import azure.functions as func
from onefuzztypes.enums import ErrorCode
from onefuzztypes.models import Error, FileEntry

from ..onefuzzlib.azure.containers import (
    blob_exists,
    container_exists,
    get_file_sas_url,
)
from ..onefuzzlib.azure.storage import StorageType
from ..onefuzzlib.endpoint_authorization import call_if_user
from ..onefuzzlib.request import not_ok, parse_uri, redirect


def get(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_uri(FileEntry, req)
    if isinstance(request, Error):
        return not_ok(request, context="download")

    if not container_exists(request.container, StorageType.corpus):
        return not_ok(
            Error(code=ErrorCode.INVALID_REQUEST, errors=["invalid container"]),
            context=request.container,
        )

    if not blob_exists(request.container, request.filename, StorageType.corpus):
        return not_ok(
            Error(code=ErrorCode.INVALID_REQUEST, errors=["invalid filename"]),
            context=request.filename,
        )

    return redirect(
        get_file_sas_url(
            request.container,
            request.filename,
            StorageType.corpus,
            read=True,
            days=0,
            minutes=5,
        )
    )


def main(req: func.HttpRequest) -> func.HttpResponse:
    methods = {"GET": get}
    method = methods[req.method]
    result = call_if_user(req, method)

    return result
