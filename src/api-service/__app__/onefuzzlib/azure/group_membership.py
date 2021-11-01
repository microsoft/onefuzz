#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import os
from typing import List, Protocol
from uuid import UUID

from .creds import query_microsoft_graph_list


class GroupMembershipChecker(Protocol):
    def is_member(self, group_ids: List[UUID], member_id: UUID) -> bool:
        """Check if member is part of at least one of the groups"""


def create_group_membership_checker() -> GroupMembershipChecker:

    memberships = os.environ.get("_STATIC_GROUP_MEMBERSHIP")
    if memberships is None:
        return AzureADGroupMembership()
    else:
        return StaticGroupMembership(memberships)


class AzureADGroupMembership:
    def is_member(self, group_ids: List[UUID], member_id: UUID) -> bool:
        if member_id in group_ids:
            return True

        body = {"groupIds": group_ids}
        response = query_microsoft_graph_list(
            method="POST", resource=f"users/{member_id}/checkMemberGroups", body=body
        )
        return group_ids in response


class StaticGroupMembership:
    def __init__(self, memberships: str):
        from pydantic import BaseModel
        from pydantic.tools import parse_raw_as

        class GroupMemebership(BaseModel):
            principal_id: UUID
            groups: List[UUID]

        data = parse_raw_as(List[GroupMemebership], memberships)
        self.memberships = data

    def is_member(self, group_ids: List[UUID], member_id: UUID) -> bool:
        if member_id in group_ids:
            return True

        for membership in self.memberships:
            if membership.principal_id == member_id:
                for group_id in group_ids:
                    if group_id not in membership.groups:
                        return False
                return True
        return False
