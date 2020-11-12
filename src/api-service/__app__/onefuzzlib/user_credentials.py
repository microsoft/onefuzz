#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from uuid import UUID

import azure.functions as func
import jwt
from onefuzztypes.enums import ErrorCode
from onefuzztypes.models import Error, Result, UserInfo


def parse_jwt_token(request: func.HttpRequest) -> Result[UserInfo]:
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

    application_id = UUID(token["appid"])
    object_id = UUID(token["oid"]) if "oid" in token else None
    upn = token.get("upn")
    return UserInfo(application_id=application_id, object_id=object_id, upn=upn)
