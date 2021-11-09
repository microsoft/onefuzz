#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
import logging
import os
from typing import TYPE_CHECKING, Optional, Sequence, Type, TypeVar, Union
from uuid import UUID

from azure.functions import HttpRequest, HttpResponse
from onefuzztypes.enums import ErrorCode
from onefuzztypes.models import Error
from onefuzztypes.responses import BaseResponse
from pydantic import ValidationError

from .orm import ModelMixin

# We don't actually use these types at runtime at this time.  Rather,
# these are used in a bound TypeVar.  MyPy suggests to only import these
# types during type checking.
if TYPE_CHECKING:
    from onefuzztypes.requests import BaseRequest  # noqa: F401
    from pydantic import BaseModel  # noqa: F401


def check_access(req: HttpRequest) -> Optional[Error]:
    if "ONEFUZZ_AAD_GROUP_ID" in os.environ:
        message = "ONEFUZZ_AAD_GROUP_ID configuration not supported"
        logging.error(message)
        return Error(
            code=ErrorCode.INVALID_CONFIGURATION,
            errors=[message],
        )
    else:
        return None


def ok(
    data: Union[BaseResponse, Sequence[BaseResponse], ModelMixin, Sequence[ModelMixin]]
) -> HttpResponse:
    if isinstance(data, BaseResponse):
        return HttpResponse(data.json(exclude_none=True), mimetype="application/json")

    if isinstance(data, list) and len(data) > 0 and isinstance(data[0], BaseResponse):
        decoded = [json.loads(x.json(exclude_none=True)) for x in data]
        return HttpResponse(json.dumps(decoded), mimetype="application/json")

    if isinstance(data, ModelMixin):
        return HttpResponse(
            data.json(exclude_none=True, exclude=data.export_exclude()),
            mimetype="application/json",
        )

    decoded = [
        x.raw(exclude_none=True, exclude=x.export_exclude())
        if isinstance(x, ModelMixin)
        else x
        for x in data
    ]
    return HttpResponse(
        json.dumps(decoded),
        mimetype="application/json",
    )


def not_ok(
    error: Error, *, status_code: int = 400, context: Union[str, UUID]
) -> HttpResponse:
    if 400 <= status_code <= 599:
        logging.error("request error - %s: %s", str(context), error.json())

        return HttpResponse(
            error.json(), status_code=status_code, mimetype="application/json"
        )
    else:
        raise Exception(
            "status code %s is not int the expected range [400; 599]" % status_code
        )


def redirect(url: str) -> HttpResponse:
    return HttpResponse(status_code=302, headers={"Location": url})


def convert_error(err: ValidationError) -> Error:
    errors = []
    for error in err.errors():
        if isinstance(error["loc"], tuple):
            name = ".".join([str(x) for x in error["loc"]])
        else:
            name = str(error["loc"])
        errors.append("%s: %s" % (name, error["msg"]))
    return Error(code=ErrorCode.INVALID_REQUEST, errors=errors)


# TODO: loosen restrictions here during dev.  We should be specific
# about only parsing things that are of a "Request" type, but there are
# a handful of types that need work in order to enforce that.
#
# These can be easily found by swapping the following comment and running
# mypy.
#
# A = TypeVar("A", bound="BaseRequest")
A = TypeVar("A", bound="BaseModel")


def parse_request(cls: Type[A], req: HttpRequest) -> Union[A, Error]:
    access = check_access(req)
    if isinstance(access, Error):
        return access

    try:
        return cls.parse_obj(req.get_json())
    except ValidationError as err:
        return convert_error(err)


def parse_uri(cls: Type[A], req: HttpRequest) -> Union[A, Error]:
    access = check_access(req)
    if isinstance(access, Error):
        return access

    data = {}
    for key in req.params:
        data[key] = req.params[key]

    try:
        return cls.parse_obj(data)
    except ValidationError as err:
        return convert_error(err)


class RequestException(Exception):
    def __init__(self, error: Error):
        self.error = error
        message = "error %s" % error
        super().__init__(message)
