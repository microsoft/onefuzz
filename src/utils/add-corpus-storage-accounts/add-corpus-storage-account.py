#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import argparse
import json
import uuid

from azure.identity import AzureCliCredential
from azure.mgmt.eventgrid import EventGridManagementClient
from azure.mgmt.eventgrid.models import EventSubscription
from azure.mgmt.resource import SubscriptionClient
from azure.mgmt.storage import StorageManagementClient
from azure.mgmt.storage.models import (
    AccessTier,
    Kind,
    Sku,
    SkuName,
    StorageAccountCreateParameters,
)

# This was generated randomly and should be preserved moving forwards
STORAGE_GUID_NAMESPACE = uuid.UUID("f7eb528c-d849-4b81-9046-e7036f6203df")


def get_base_event(
    client: EventGridManagementClient, resource_group: str, location: str
) -> EventSubscription:
    for entry in client.event_subscriptions.list_regional_by_resource_group(
        resource_group, location
    ):
        if (
            entry.name == "onefuzz1_subscription"
            and entry.type == "Microsoft.EventGrid/eventSubscriptions"
            and entry.event_delivery_schema == "EventGridSchema"
            and entry.destination.endpoint_type == "StorageQueue"
            and entry.destination.queue_name == "file-changes"
        ):
            return entry

    raise Exception("unable to find base eventgrid subscription")


def add_event_grid(src_account_id: str, resource_group: str, location: str) -> None:
    credential = AzureCliCredential()
    client = EventGridManagementClient(credential)
    base = get_base_event(client, resource_group, location)

    event_subscription_info = EventSubscription(
        destination=base.destination,
        filter=base.filter,
        retry_policy=base.retry_policy,
    )

    topic_id = uuid.uuid5(STORAGE_GUID_NAMESPACE, src_account_id).hex

    result = client.event_subscriptions.begin_create_or_update(
        src_account_id, "corpus" + topic_id, event_subscription_info
    ).result()
    if result.provisioning_state != "Succeeded":
        raise Exception(
            "eventgrid subscription failed: %s"
            % json.dumps(result.as_dict(), indent=4, sort_keys=True),
        )


def create_storage(resource_group: str, account_name: str, location: str) -> str:
    params = StorageAccountCreateParameters(
        sku=Sku(name=SkuName.premium_lrs),
        kind=Kind.block_blob_storage,
        location=location,
        tags={"storage_type": "corpus"},
        access_tier=AccessTier.hot,
        allow_blob_public_access=False,
        minimum_tls_version="TLS1_2",
    )

    credential = AzureCliCredential()
    client = StorageManagementClient(credential)
    account = client.storage_accounts.begin_create(
        resource_group, account_name, params
    ).result()
    if account.provisioning_state != "Succeeded":
        raise Exception(
            "storage account creation failed: %s",
            json.dumps(account.as_dict(), indent=4, sort_keys=True),
        )
    return account.id


def create(resource_group: str, account_name: str, location: str) -> None:
    new_account_id = create_storage(resource_group, account_name, location)
    add_event_grid(new_account_id, resource_group, location)


def main():
    formatter = argparse.ArgumentDefaultsHelpFormatter
    parser = argparse.ArgumentParser(formatter_class=formatter)
    parser.add_argument("resource_group")
    parser.add_argument("account_name")
    parser.add_argument("location")
    args = parser.parse_args()

    create(args.resource_group, args.account_name, args.location)


if __name__ == "__main__":
    main()
