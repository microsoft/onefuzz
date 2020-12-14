#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import azure.functions as func
from onefuzztypes.models import Error
from onefuzztypes.requests import DownloadConfigRequest
from onefuzztypes.responses import DownloadConfig

from ..onefuzzlib.agent_authorization import call_if_agent
from ..onefuzzlib.azure.containers import StorageType, get_container_sas_url
from ..onefuzzlib.request import not_ok, ok, parse_uri


def get(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_uri(DownloadConfigRequest, req)
    if isinstance(request, Error):
        return not_ok(request, context="DownloadConfigRequest")

    tools_sas = get_container_sas_url("tools", StorageType.config, read=True, list=True)

    return ok(DownloadConfig(tools=tools_sas))


def main(req: func.HttpRequest) -> func.HttpResponse:
    if req.method == "GET":
        m = get
    else:
        raise Exception("invalid method")

    return call_if_agent(req, m)
