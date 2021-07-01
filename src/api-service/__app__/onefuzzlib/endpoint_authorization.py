#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from typing import Callable, Optional
from uuid import UUID

import azure.functions as func
from memoization import cached
from onefuzztypes.enums import ErrorCode
from onefuzztypes.models import Error, UserInfo

from .azure.creds import get_scaleset_principal_id
from .config import InstanceConfig
from .request import not_ok
from .user_credentials import parse_jwt_token
from .workers.pools import Pool
from .workers.scalesets import Scaleset


@cached(ttl=60)
def is_agent(token_data: UserInfo) -> bool:

    if token_data.object_id:
        # backward compatibility case for scalesets deployed before the migration
        # to user assigned managed id
        scalesets = Scaleset.get_by_object_id(token_data.object_id)
        if len(scalesets) > 0:
            return True

        # verify object_id against the user assigned managed identity
        principal_id: UUID = get_scaleset_principal_id()
        return principal_id == token_data.object_id

    if not token_data.application_id:
        return False

    pools = Pool.search(query={"client_id": [token_data.application_id]})
    if len(pools) > 0:
        return True

    return False


def can_modify_config_impl(config: InstanceConfig, user_info: UserInfo) -> bool:
    if config.admins is None:
        return True

    return user_info.object_id in config.admins


def can_modify_config(req: func.HttpRequest, config: InstanceConfig) -> bool:
    user_info = parse_jwt_token(req)
    if not isinstance(user_info, UserInfo):
        return False

    return can_modify_config_impl(config, user_info)


def check_can_manage_pools_impl(
    config: InstanceConfig, user_info: UserInfo
) -> Optional[Error]:
    if config.allow_pool_management:
        return None

    if config.admins is None:
        return Error(code=ErrorCode.UNAUTHORIZED, errors=["pool modification disabled"])

    if user_info.object_id in config.admins:
        return None

    return Error(code=ErrorCode.UNAUTHORIZED, errors=["not authorized to manage pools"])


def check_can_manage_pools(req: func.HttpRequest) -> Optional[Error]:
    user_info = parse_jwt_token(req)
    if isinstance(user_info, Error):
        return user_info

    # When there are no admins in the `admins` list, all users are considered
    # admins.  However, `allow_pool_management` is still useful to protect from
    # mistakes.
    #
    # To make changes while still protecting against accidental changes to
    # pools, do the following:
    #
    # 1. set `allow_pool_management` to `True`
    # 2. make the change
    # 3. set `allow_pool_management` to `False`

    config = InstanceConfig.fetch()

    return check_can_manage_pools_impl(config, user_info)


def is_user(token_data: UserInfo) -> bool:
    return not is_agent(token_data)


def reject(req: func.HttpRequest, token: UserInfo) -> func.HttpResponse:
    logging.error(
        "reject token.  url:%s token:%s body:%s",
        repr(req.url),
        repr(token),
        repr(req.get_body()),
    )
    return not_ok(
        Error(code=ErrorCode.UNAUTHORIZED, errors=["Unrecognized agent"]),
        status_code=401,
        context="token verification",
    )


def call_if(
    req: func.HttpRequest,
    method: Callable[[func.HttpRequest], func.HttpResponse],
    *,
    allow_user: bool = False,
    allow_agent: bool = False
) -> func.HttpResponse:
    token = parse_jwt_token(req)
    if isinstance(token, Error):
        return not_ok(token, status_code=401, context="token verification")

    if is_user(token) and not allow_user:
        return reject(req, token)

    if is_agent(token) and not allow_agent:
        return reject(req, token)

    return method(req)


def call_if_user(
    req: func.HttpRequest, method: Callable[[func.HttpRequest], func.HttpResponse]
) -> func.HttpResponse:

    return call_if(req, method, allow_user=True)


def call_if_agent(
    req: func.HttpRequest, method: Callable[[func.HttpRequest], func.HttpResponse]
) -> func.HttpResponse:

    return call_if(req, method, allow_agent=True)
