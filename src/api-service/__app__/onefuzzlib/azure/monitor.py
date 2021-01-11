#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import os
from typing import Any, Dict

from azure.mgmt.loganalytics import OperationalInsightsManagementClient
from memoization import cached

from .creds import get_base_resource_group, mgmt_client_factory


@cached(ttl=60)
def get_montior_client() -> Any:
    return mgmt_client_factory(OperationalInsightsManagementClient)


@cached(ttl=60)
def get_monitor_settings() -> Dict[str, str]:
    resource_group = get_base_resource_group()
    workspace_name = os.environ["ONEFUZZ_MONITOR"]
    client = get_montior_client()
    customer_id = client.workspaces.get(resource_group, workspace_name).customer_id
    shared_key = client.shared_keys.get_shared_keys(
        resource_group, workspace_name
    ).primary_shared_key
    return {"id": customer_id, "key": shared_key}
