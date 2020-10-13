#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import azure.functions as func
from onefuzztypes.models import Error
from onefuzztypes.requests import DownloadConfigRequest
from onefuzztypes.responses import DownloadConfig

from ..onefuzzlib.azure.containers import get_container_sas_url
from ..onefuzzlib.azure.creds import get_func_storage
from ..onefuzzlib.agent_authorization import verify_token
from ..onefuzzlib.pools import Node
from ..onefuzzlib.request import not_ok, ok, parse_request
from ..onefuzzlib.tasks.main import Task


def get(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(DownloadConfigRequest, req)
    if isinstance(request, Error):
        return not_ok(request, context="DownloadConfigRequest")

    tools_sas = get_container_sas_url(
        "tools", read=True, list=True, account_id=get_func_storage()
    )

    return ok(DownloadConfig(tools=tools_sas))


def main(req: func.HttpRequest) -> func.HttpResponse:
    if req.method == "GET":
        m = get
    else:
        raise Exception("invalid method")

    return verify_token(req, m)