#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from typing import List, Optional
from uuid import UUID

import azure.functions as func
import jwt
from memoization import cached
from onefuzztypes.enums import ErrorCode
from onefuzztypes.models import Error, Result, UserInfo

from .config import InstanceConfig


def get_bearer_token(request: func.HttpRequest) -> Optional[str]:
    auth: str = request.headers.get("Authorization", None)
    if not auth:
        return None

    parts = auth.split()

    if len(parts) != 2:
        return None

    if parts[0].lower() != "bearer":
        return None

    return parts[1]


def get_auth_token(request: func.HttpRequest) -> Optional[str]:
    token = get_bearer_token(request)
    if token is not None:
        return token

    token_header = request.headers.get("x-ms-token-aad-id-token", None)
    if token_header is None:
        return None
    return str(token_header)


@cached(ttl=60)
def get_allowed_tenants() -> List[str]:
    config = InstanceConfig.fetch()
    entries = [f"https://sts.windows.net/{x}" for x in config.allowed_aad_tenants]
    return entries


def parse_jwt_token(request: func.HttpRequest) -> Result[UserInfo]:
    """Obtains the Access Token from the Authorization Header"""
    token_str = get_auth_token(request)
    if token_str is None:
        return Error(
            code=ErrorCode.INVALID_REQUEST,
            errors=["unable to find authorization token"],
        )

    # The JWT token has already been verified by the azure authentication layer,
    # but we need to verify the tenant is as we expect.
    token = jwt.decode(token_str, options={"verify_signature": False})

    if "iss" not in token:
        return Error(
            code=ErrorCode.INVALID_REQUEST, errors=["missing issuer from token"]
        )

    if token["iss"] not in get_allowed_tenants():
        return Error(code=ErrorCode.INVALID_REQUEST, errors=["unauthorized AAD issuer"])

    application_id = UUID(token["appid"]) if "appid" in token else None
    object_id = UUID(token["oid"]) if "oid" in token else None
    upn = token.get("upn")
    return UserInfo(application_id=application_id, object_id=object_id, upn=upn)
