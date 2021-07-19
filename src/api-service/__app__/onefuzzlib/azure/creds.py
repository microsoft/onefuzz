#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import os
import urllib.parse
from typing import Any, Dict, List, Optional
from uuid import UUID

import requests
from azure.identity import DefaultAzureCredential
from azure.keyvault.secrets import SecretClient
from azure.mgmt.resource import ResourceManagementClient
from azure.mgmt.subscription import SubscriptionClient
from memoization import cached
from msrestazure.azure_active_directory import AZURE_PUBLIC_CLOUD, MSIAuthentication
from msrestazure.tools import parse_resource_id
from onefuzztypes.primitives import Container, Region

from .monkeypatch import allow_more_workers, reduce_logging


@cached
def get_ms_graph_msi() -> MSIAuthentication:
    allow_more_workers()
    reduce_logging()
    return MSIAuthentication(
        resource=AZURE_PUBLIC_CLOUD.endpoints.microsoft_graph_resource_id
    )


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


def query_microsoft_graph(
    method: str,
    resource: str,
    params: Optional[Dict] = None,
    body: Optional[Dict] = None,
) -> Any:
    auth = get_ms_graph_msi()
    access_token = auth.token["access_token"]
    token_type = auth.token["token_type"]

    url = urllib.parse.urljoin("https://graph.microsoft.com/v1.0/", resource)
    headers = {
        "Authorization": "%s %s" % (token_type, access_token),
        "Content-Type": "application/json",
    }
    response = requests.request(
        method=method, url=url, headers=headers, params=params, json=body
    )

    response.status_code

    if 200 <= response.status_code < 300:
        try:
            return response.json()
        except ValueError:
            return None
    else:
        error_text = str(response.content, encoding="utf-8", errors="backslashreplace")
        raise Exception(
            "request did not succeed: HTTP %s - %s"
            % (response.status_code, error_text),
            response.status_code,
        )


######## FOR TESTING PURPOSE ONLY ############
def is_member_of_test(group_ids: List[UUID], member_id: UUID) -> bool:
    from pydantic import BaseModel
    from pydantic.tools import parse_obj_as

    class GroupMemebership(BaseModel):
        principal_id: UUID
        groups: List[UUID]

    if os.environ["TEST_MSGRAPH_AAD"]:
        data = parse_obj_as(List[GroupMemebership], os.environ["TEST_MSGRAPH_AAD"])
        for membership in data:
            if membership.principal_id == member_id:
                for group_id in group_ids:
                    if group_id not in membership.groups:
                        return False
                return True
        return False


#########################################


def is_member_of(group_ids: List[UUID], member_id: UUID) -> bool:
    body = {"groupIds": group_ids}
    response = query_microsoft_graph(
        method="POST", resource=f"users/{member_id}/checkMemberGroups", body=body
    )
    result = map(UUID, response["value"])
    for group_id in group_ids:
        if group_id not in result:
            return False

    return True


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
