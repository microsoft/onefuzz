#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from typing import Callable, Union
from uuid import UUID

import azure.functions as func
import jwt
from memoization import cached
from onefuzztypes.enums import ErrorCode
from onefuzztypes.models import Error
from pydantic import BaseModel

from .pools import Scaleset
from .request import not_ok


class TokenData(BaseModel):
    application_id: UUID
    object_id: UUID


def try_get_token_auth_header(request: func.HttpRequest) -> Union[Error, TokenData]:
    """ Obtains the Access Token from the Authorization Header """
    auth: str = request.headers.get("Authorization", None)
    if not auth:
        return Error(
            code=ErrorCode.INVALID_REQUEST, errors=["Authorization header is expected"]
        )
    parts = auth.split()

    if parts[0].lower() != "bearer":
        return Error(
            code=ErrorCode.INVALID_REQUEST,
            errors=["Authorization header must start with Bearer"],
        )

    elif len(parts) == 1:
        return Error(code=ErrorCode.INVALID_REQUEST, errors=["Token not found"])

    elif len(parts) > 2:
        return Error(
            code=ErrorCode.INVALID_REQUEST,
            errors=["Authorization header must be Bearer token"],
        )

    # This token has already been verified by the azure authentication layer
    token = jwt.decode(parts[1], verify=False)
    return TokenData(application_id=UUID(token["appid"]), object_id=UUID(token["oid"]))


@cached(ttl=60)
def is_authorized(token_data: TokenData) -> bool:
    scalesets = Scaleset.get_by_object_id(token_data.object_id)
    return len(scalesets) > 0


def verify_token(
    req: func.HttpRequest, func: Callable[[func.HttpRequest], func.HttpResponse]
) -> func.HttpResponse:
    token = try_get_token_auth_header(req)

    if isinstance(token, Error):
        return not_ok(token, status_code=401, context="token verification")

    if not is_authorized(token):
        logging.error(
            "rejecting token url:%s token:%s body:%s",
            repr(req.url),
            repr(token),
            repr(req.get_body()),
        )
        return not_ok(
            Error(code=ErrorCode.UNAUTHORIZED, errors=["Unrecognized agent"]),
            status_code=401,
            context="token verification",
        )

    return func(req)
