#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import ipaddress
import json
import logging
from typing import Any, Dict, List, Optional
from uuid import UUID

from azure.cosmosdb.table.tableservice import TableService

storage_client_logger = logging.getLogger("azure.cosmosdb.table.common.storageclient")
TABLE_NAME = "InstanceConfig"

logger = logging.getLogger("deploy")


class InstanceConfigClient:

    table_service: TableService
    resource_group: str

    def __init__(self, table_service: TableService, resource_group: str):
        self.resource_group = resource_group
        self.table_service = table_service
        self.create_if_missing(table_service)

    ## Disable logging from storageclient. This module displays an error message
    ## when a resource is not found even if the exception is raised and handled internally.
    ## This happen when a table does not exist. An error message is displayed but the exception is
    ## handled by the library.
    def disable_storage_client_logging(self) -> None:
        if storage_client_logger:
            storage_client_logger.disabled = True

    def enable_storage_client_logging(self) -> None:
        if storage_client_logger:
            storage_client_logger.disabled = False

    def create_if_missing(self, table_service: TableService) -> None:
        try:
            self.disable_storage_client_logging()

            if not table_service.exists(TABLE_NAME):
                table_service.create_table(TABLE_NAME)
        finally:
            self.enable_storage_client_logging()


class NetworkSecurityConfig:
    allowed_ips: List[str]
    allowed_service_tags: List[str]

    def __init__(self, config: Any):
        self.parse_nsg_json(config)

    def parse_nsg_json(self, config: Any) -> None:

        if len(config.keys()) == 0:
            raise Exception(
                "Empty Configuration File Provided. Please Provide Valid Config."
            )
        if None in config.keys() or "proxy_nsg_config" not in config.keys():
            raise Exception(
                "proxy_nsg_config not provided as valid key. Please Provide Valid Config."
            )
        proxy_config = config["proxy_nsg_config"]
        if len(proxy_config.keys()) == 0:
            raise Exception(
                "Empty Configuration File Provided. Please Provide Valid Config."
            )
        if (
            None in proxy_config.keys()
            or "allowed_ips" not in proxy_config.keys()
            or "allowed_serviice_tags" not in proxy_config.keys()
        ):
            raise Exception(
                "allowed_ips and allowed_service_tags not provided. Please Provide Valid Config."
            )
        if not isinstance(proxy_config["allowed_ips"], List) or not isinstance(
            proxy_config["allowed_service_tags"]
        ):
            raise Exception(
                "allowed_ips and allowed_service_tags are not a list. Please Provide Valid Config."
            )

        if not all(isinstance(x, str) for x in proxy_config["allowed_ips"]) or not all(
            isinstance(x, str) for x in proxy_config["allowed_service_tags"]
        ):
            raise Exception(
                "allowed_ips and allowed_service_tags are not a list of strings. Please Provide Valid Config."
            )

        self.allowed_ips = proxy_config["allowed_ips"]
        self.allowed_service_tags = proxy_config["allowed_service_tags"]


class NsgRule:

    rule: str
    is_tag: bool

    def __init__(self, rule: str):
        try:
            self.is_tag = False
            self.check_rule(rule)
            self.rule = rule
        except Exception:
            raise ValueError(
                "Invalid rule. Please provide a valid rule or supply the wild card *."
            )

    def check_rule(self, value: str) -> None:
        if value is None or len(value.strip()) == 0:
            raise ValueError(
                "Please provide a valid rule. Supply an empty list to block all sources or the wild card * to allow all sources."
            )
        # Check Wild Card
        if value == "*":
            return
        # Check if IP Address
        try:
            ipaddress.ip_address(value)
            return
        except ValueError:
            pass
        # Check if IP Range
        try:
            ipaddress.ip_network(value, False)
            return
        except ValueError:
            pass

        self.is_tag = True


def update_allowed_aad_tenants(
    config_client: InstanceConfigClient, tenants: List[UUID]
) -> None:
    as_str = [str(x) for x in tenants]
    config_client.table_service.insert_or_merge_entity(
        TABLE_NAME,
        {
            "PartitionKey": config_client.resource_group,
            "RowKey": config_client.resource_group,
            "allowed_aad_tenants": json.dumps(as_str),
        },
    )


def update_admins(config_client: InstanceConfigClient, admins: List[UUID]) -> None:
    admins_as_str: Optional[List[str]] = None
    if admins:
        admins_as_str = [str(x) for x in admins]

    config_client.table_service.insert_or_merge_entity(
        TABLE_NAME,
        {
            "PartitionKey": config_client.resource_group,
            "RowKey": config_client.resource_group,
            "admins": json.dumps(admins_as_str),
        },
    )


def parse_rules(proxy_config: NetworkSecurityConfig) -> List[NsgRule]:

    allowed_ips = proxy_config["allowed_ips"]
    allowed_service_tags = proxy_config["allowed_service_tags"]

    nsg_rules = []
    if "*" in allowed_ips:
        nsg_rule = NsgRule("*")
        nsg_rules.append(nsg_rule)
        return nsg_rules
    elif len(allowed_ips) + len(allowed_service_tags) == 0:
        return []

    for rule in allowed_ips:
        try:
            nsg_rule = NsgRule(rule)
            nsg_rules.append(nsg_rule)
        except Exception:
            raise ValueError(
                "One or more input ips was invalid: %s. Please enter a comma-separted list of valid sources.",
                rule,
            )
    for rule in allowed_service_tags:
        try:
            nsg_rule = NsgRule(rule)
            nsg_rules.append(nsg_rule)
        except Exception:
            raise ValueError(
                "One or more input tags was invalid: %s. Please enter a comma-separted list of valid sources.",
                rule,
            )
    return nsg_rules


def update_nsg(
    config_client: InstanceConfigClient,
    allowed_rules: List[NsgRule],
) -> None:
    tags_as_str = [x.rule for x in allowed_rules if x.is_tag]
    ips_as_str = [x.rule for x in allowed_rules if not x.is_tag]
    nsg_config = {"allowed_service_tags": tags_as_str, "allowed_ips": ips_as_str}
    # create class initialized by table service/resource group outside function that's checked in deploy.py
    config_client.table_service.insert_or_merge_entity(
        TABLE_NAME,
        {
            "PartitionKey": config_client.resource_group,
            "RowKey": config_client.resource_group,
            "proxy_nsg_config": json.dumps(nsg_config),
        },
    )


if __name__ == "__main__":
    pass
