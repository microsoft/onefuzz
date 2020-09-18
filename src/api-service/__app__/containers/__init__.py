#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from typing import Optional, Union

import azure.functions as func
from onefuzztypes.enums import ErrorCode
from onefuzztypes.models import Error
from onefuzztypes.requests import ContainerCreate, ContainerDelete, ContainerGet
from onefuzztypes.responses import BoolResult, ContainerInfo, ContainerInfoBase

from ..onefuzzlib.azure.containers import (
    create_container,
    delete_container,
    get_container_sas_url,
    get_containers,
)
from ..onefuzzlib.request import not_ok, ok, parse_request


def get(req: func.HttpRequest) -> func.HttpResponse:
    request: Optional[Union[ContainerGet, Error]] = None
    if req.get_body():
        request = parse_request(ContainerGet, req)

    if isinstance(request, Error):
        return not_ok(request, context="container get")

    containers = get_containers()

    if request is not None:
        if request.name in containers:
            info = ContainerInfo(
                name=request.name,
                sas_url=get_container_sas_url(
                    request.name,
                    read=True,
                    write=True,
                    create=True,
                    delete=True,
                    list=True,
                ),
                metadata=containers[request.name],
            )
            return ok(info)
        return not_ok(
            Error(code=ErrorCode.INVALID_REQUEST, errors=["invalid container"]),
            context=request.name,
        )

    container_info = []
    for name in containers:
        container_info.append(ContainerInfoBase(name=name, metadata=containers[name]))

    return ok(container_info)


def post(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(ContainerCreate, req)
    if isinstance(request, Error):
        return not_ok(request, context="container create")

    logging.info("container - creating %s", request.name)
    sas = create_container(request.name, metadata=request.metadata)
    if sas:
        return ok(
            ContainerInfo(name=request.name, sas_url=sas, metadata=request.metadata)
        )
    return not_ok(
        Error(code=ErrorCode.INVALID_REQUEST, errors=["invalid container"]),
        context=request.name,
    )


def delete(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(ContainerDelete, req)
    if isinstance(request, Error):
        return not_ok(request, context="container delete")

    logging.info("container - deleting %s", request.name)
    return ok(BoolResult(result=delete_container(request.name)))


def main(req: func.HttpRequest) -> func.HttpResponse:
    methods = {"GET": get, "POST": post, "DELETE": delete}
    return methods[req.method](req)
