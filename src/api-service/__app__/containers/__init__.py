#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from typing import Optional, Union

import azure.functions as func
from onefuzztypes.enums import ErrorCode
from onefuzztypes.models import Error
from onefuzztypes.requests import (
    ContainerCreate,
    ContainerDelete,
    ContainerGet,
    ContainerUpdate,
)
from onefuzztypes.responses import BoolResult, ContainerInfo, ContainerInfoBase

from ..onefuzzlib.azure.containers import (
    create_container,
    delete_container,
    get_container_detail,
    get_container_sas_url,
    get_containers,
    set_container_detail,
)
from ..onefuzzlib.azure.storage import StorageType
from ..onefuzzlib.endpoint_authorization import call_if_user
from ..onefuzzlib.events import get_events
from ..onefuzzlib.request import not_ok, ok, parse_request


def get(req: func.HttpRequest) -> func.HttpResponse:
    request: Optional[Union[ContainerGet, Error]] = None
    if req.get_body():
        request = parse_request(ContainerGet, req)

    if isinstance(request, Error):
        return not_ok(request, context="container get")
    if request is not None:
        detail = get_container_detail(request.name, StorageType.corpus)
        if detail is None:
            return not_ok(
                Error(code=ErrorCode.INVALID_REQUEST, errors=["invalid container"]),
                context=request.name,
            )

        info = ContainerInfo(
            name=request.name,
            sas_url=get_container_sas_url(
                request.name,
                StorageType.corpus,
                read=True,
                write=True,
                delete=True,
                list=True,
            ),
            metadata=detail.metadata,
            holds=detail.holds,
        )
        return ok(info)

    containers = get_containers(StorageType.corpus)

    container_info = []
    for name in containers:
        container_info.append(ContainerInfoBase(name=name, metadata=containers[name]))

    return ok(container_info)


def post(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(ContainerCreate, req)
    if isinstance(request, Error):
        return not_ok(request, context="container create")

    logging.info("container - creating %s", request.name)
    sas = create_container(request.name, StorageType.corpus, metadata=request.metadata)
    if sas:
        return ok(
            ContainerInfo(
                name=request.name, sas_url=sas, metadata=request.metadata, holds=[]
            )
        )
    return not_ok(
        Error(code=ErrorCode.INVALID_REQUEST, errors=["invalid container"]),
        context=request.name,
    )


def patch(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(ContainerUpdate, req)
    if isinstance(request, Error):
        return not_ok(request, context="container update")

    logging.info("container - updating %s", request.name)
    result = set_container_detail(
        request.name, StorageType.corpus, metadata=request.metadata, holds=request.holds
    )
    if result is None:
        return not_ok(
            Error(code=ErrorCode.INVALID_REQUEST, errors=["invalid container"]),
            context=request.name,
        )
    return ok(BoolResult(result=result))


def delete(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(ContainerDelete, req)
    if isinstance(request, Error):
        return not_ok(request, context="container delete")

    logging.info("container - deleting %s", request.name)
    return ok(BoolResult(result=delete_container(request.name, StorageType.corpus)))


def main(req: func.HttpRequest, dashboard: func.Out[str]) -> func.HttpResponse:
    methods = {"GET": get, "POST": post, "DELETE": delete}
    method = methods[req.method]
    result = call_if_user(req, method)

    events = get_events()
    if events:
        dashboard.set(events)

    return result
