#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import azure.functions as func
from onefuzztypes.enums import ErrorCode, VmState
from onefuzztypes.models import Error, ReproConfig
from onefuzztypes.requests import ReproGet

from ..onefuzzlib.endpoint_authorization import call_if_user
from ..onefuzzlib.repro import Repro
from ..onefuzzlib.request import not_ok, ok, parse_request
from ..onefuzzlib.user_credentials import parse_jwt_token


def get(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(ReproGet, req)
    if isinstance(request, Error):
        return not_ok(request, context="repro_vm get")

    if request.vm_id:
        vm = Repro.get(request.vm_id)
        if not vm:
            return not_ok(
                Error(code=ErrorCode.INVALID_REQUEST, errors=["no such VM"]),
                context=request.vm_id,
            )
        return ok(vm)
    else:
        vms = Repro.search_states(states=VmState.available())
        for vm in vms:
            vm.auth = None
        return ok(vms)


def post(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(ReproConfig, req)
    if isinstance(request, Error):
        return not_ok(request, context="repro_vm create")

    user_info = parse_jwt_token(req)
    if isinstance(user_info, Error):
        return not_ok(user_info, context="task create")

    vm = Repro.create(request, user_info)
    if isinstance(vm, Error):
        return not_ok(vm, context="repro_vm create")

    return ok(vm)


def delete(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(ReproGet, req)
    if isinstance(request, Error):
        return not_ok(request, context="repro delete")

    if not request.vm_id:
        return not_ok(
            Error(code=ErrorCode.INVALID_REQUEST, errors=["missing vm_id"]),
            context="repro delete",
        )

    vm = Repro.get(request.vm_id)
    if not vm:
        return not_ok(
            Error(code=ErrorCode.INVALID_REQUEST, errors=["no such VM"]),
            context=request.vm_id,
        )

    vm.state = VmState.stopping
    vm.save()
    return ok(vm)


def main(req: func.HttpRequest) -> func.HttpResponse:
    methods = {"GET": get, "POST": post, "DELETE": delete}
    method = methods[req.method]
    result = call_if_user(req, method)

    return result
