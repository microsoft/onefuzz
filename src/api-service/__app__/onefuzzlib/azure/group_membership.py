#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from typing import Dict, List, Protocol
from uuid import UUID

from ..config import InstanceConfig
from .creds import query_microsoft_graph_list


class GroupMembershipChecker(Protocol):
    def is_member(self, group_ids: List[UUID], member_id: UUID) -> bool:
        """Check if member is part of at least one of the groups"""
        if member_id in group_ids:
            return True

        groups = self.get_groups(member_id)
        return group_ids in groups

    def get_groups(self, member_id: UUID) -> List[UUID]:
        """Gets all the groups of the provided member"""


def create_group_membership_checker() -> GroupMembershipChecker:
    config = InstanceConfig.fetch()
    if config.group_membership:
        return StaticGroupMembership(config.group_membership)
    else:
        return AzureADGroupMembership()


class AzureADGroupMembership(GroupMembershipChecker):
    def get_groups(self, member_id: UUID) -> List[UUID]:
        response = query_microsoft_graph_list(
            method="GET", resource=f"users/{member_id}/memberOf"
        )
        return response


class StaticGroupMembership(GroupMembershipChecker):
    def __init__(self, memberships: Dict[UUID, List[UUID]]):
        self.memberships = memberships

    def get_groups(self, member_id: UUID) -> List[UUID]:
        return self.memberships.get(member_id, [])
