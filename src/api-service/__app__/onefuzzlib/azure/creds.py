#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import functools
import logging
import os
import urllib.parse
from typing import Any, Callable, Dict, List, Optional, TypeVar, cast
from uuid import UUID

import requests
from azure.core.exceptions import ClientAuthenticationError
from azure.identity import DefaultAzureCredential
from azure.keyvault.secrets import SecretClient
from azure.mgmt.resource import ResourceManagementClient
from azure.mgmt.subscription import SubscriptionClient
from memoization import cached
from msrestazure.azure_active_directory import MSIAuthentication
from msrestazure.tools import parse_resource_id
from onefuzztypes.primitives import Container, Region

from .monkeypatch import allow_more_workers, reduce_logging

# https://docs.microsoft.com/en-us/graph/api/overview?view=graph-rest-1.0
GRAPH_RESOURCE = "https://graph.microsoft.com"
GRAPH_RESOURCE_ENDPOINT = "https://graph.microsoft.com/v1.0"


@cached
def get_msi() -> MSIAuthentication:
    allow_more_workers()
    reduce_logging()
    return MSIAuthentication()


@cached
def get_identity() -> DefaultAzureCredential:
    allow_more_workers()
    reduce_logging()
    return DefaultAzureCredential()


@cached
def get_base_resource_group() -> Any:  # should be str
    return parse_resource_id(os.environ["ONEFUZZ_RESOURCE_GROUP"])["resource_group"]


@cached
def get_base_region() -> Region:
    client = ResourceManagementClient(
        credential=get_identity(), subscription_id=get_subscription()
    )
    group = client.resource_groups.get(get_base_resource_group())
    return Region(group.location)


@cached
def get_subscription() -> Any:  # should be str
    return parse_resource_id(os.environ["ONEFUZZ_DATA_STORAGE"])["subscription"]


@cached
def get_insights_instrumentation_key() -> Any:  # should be str
    return os.environ["APPINSIGHTS_INSTRUMENTATIONKEY"]


@cached
def get_insights_appid() -> str:
    return os.environ["APPINSIGHTS_APPID"]


@cached
def get_instance_name() -> str:
    return os.environ["ONEFUZZ_INSTANCE_NAME"]


@cached
def get_instance_url() -> str:
    return "https://%s.azurewebsites.net" % get_instance_name()


@cached
def get_instance_id() -> UUID:
    from .containers import get_blob
    from .storage import StorageType

    blob = get_blob(Container("base-config"), "instance_id", StorageType.config)
    if blob is None:
        raise Exception("missing instance_id")
    return UUID(blob.decode())


DAY_IN_SECONDS = 60 * 60 * 24


@cached(ttl=DAY_IN_SECONDS)
def get_regions() -> List[Region]:
    subscription = get_subscription()
    client = SubscriptionClient(credential=get_identity())
    locations = client.subscriptions.list_locations(subscription)
    return sorted([Region(x.name) for x in locations])


class GraphQueryError(Exception):
    def __init__(self, message: str, status_code: Optional[int]) -> None:
        super(GraphQueryError, self).__init__(message)
        self.message = message
        self.status_code = status_code


def query_microsoft_graph(
    method: str,
    resource: str,
    params: Optional[Dict] = None,
    body: Optional[Dict] = None,
) -> Dict:
    cred = get_identity()
    access_token = cred.get_token(f"{GRAPH_RESOURCE}/.default")

    url = urllib.parse.urljoin(f"{GRAPH_RESOURCE_ENDPOINT}/", resource)
    headers = {
        "Authorization": "Bearer %s" % access_token.token,
        "Content-Type": "application/json",
    }
    response = requests.request(
        method=method, url=url, headers=headers, params=params, json=body
    )

    if 200 <= response.status_code < 300:
        if response.content and response.content.strip():
            return cast(Dict, response.json())
        else:
            return {}
    else:
        error_text = str(response.content, encoding="utf-8", errors="backslashreplace")
        raise GraphQueryError(
            f"request did not succeed: HTTP {response.status_code} - {error_text}",
            response.status_code,
        )


def query_microsoft_graph_list(
    method: str,
    resource: str,
    params: Optional[Dict] = None,
    body: Optional[Dict] = None,
) -> List[Dict]:
    result = query_microsoft_graph(
        method,
        resource,
        params,
        body,
    )
    if result["value"]:
        return cast(List[Dict], result["value"])
    else:
        raise GraphQueryError("Expected data containing a list of values", None)


def is_member_of(group_id: str, member_id: str) -> bool:
    body = {"groupIds": [group_id]}
    response = query_microsoft_graph_list(
        method="POST", resource=f"users/{member_id}/checkMemberGroups", body=body
    )
    return group_id in response


@cached
def get_scaleset_identity_resource_path() -> str:
    scaleset_id_name = "%s-scalesetid" % get_instance_name()
    resource_group_path = "/subscriptions/%s/resourceGroups/%s/providers" % (
        get_subscription(),
        get_base_resource_group(),
    )
    return "%s/Microsoft.ManagedIdentity/userAssignedIdentities/%s" % (
        resource_group_path,
        scaleset_id_name,
    )


@cached
def get_scaleset_principal_id() -> UUID:
    api_version = "2018-11-30"  # matches the apiversion in the deployment template
    client = ResourceManagementClient(
        credential=get_identity(), subscription_id=get_subscription()
    )
    uid = client.resources.get_by_id(get_scaleset_identity_resource_path(), api_version)
    return UUID(uid.properties["principalId"])


@cached
def get_keyvault_client(vault_url: str) -> SecretClient:
    return SecretClient(vault_url=vault_url, credential=DefaultAzureCredential())


def clear_azure_client_cache() -> None:
    # clears the memoization of the Azure clients.

    from .compute import get_compute_client
    from .containers import get_blob_service
    from .network_mgmt_client import get_network_client
    from .storage import get_mgmt_client

    # currently memoization.cache does not project the wrapped function's types.
    # As a workaround, CI comments out the `cached` wrapper, then runs the type
    # validation.  This enables calling the wrapper's clear_cache if it's not
    # disabled.
    for func in [
        get_msi,
        get_identity,
        get_compute_client,
        get_blob_service,
        get_network_client,
        get_mgmt_client,
    ]:
        clear_func = getattr(func, "clear_cache", None)
        if clear_func is not None:
            clear_func()


T = TypeVar("T", bound=Callable[..., Any])


class retry_on_auth_failure:
    def __call__(self, func: T) -> T:
        @functools.wraps(func)
        def decorated(*args, **kwargs):  # type: ignore
            try:
                return func(*args, **kwargs)
            except ClientAuthenticationError as err:
                logging.warning(
                    "clearing authentication cache after auth failure: %s", err
                )

            clear_azure_client_cache()
            return func(*args, **kwargs)

        return cast(T, decorated)
