import unittest
import uuid

from onefuzztypes.models import ApiAccessRule

from __app__.onefuzzlib.azure.group_membership import (
    GroupMembershipChecker,
    StaticGroupMembership,
)


class TestRequestAccess(unittest.TestCase):
    def test_empty(self) -> None:
        group_id = uuid.uuid4()
        user_id = uuid.uuid4()
        checker: GroupMembershipChecker = StaticGroupMembership({})

        self.assertFalse(checker.is_member([group_id], user_id))
        self.assertTrue(checker.is_member([user_id], user_id))

    def test_matching_user_id(self) -> None:
        group_id = uuid.uuid4()
        user_id1 = uuid.uuid4()
        user_id2 = uuid.uuid4()

        checker: GroupMembershipChecker = StaticGroupMembership(
            {str(user_id1): [group_id]}
        )
        self.assertTrue(checker.is_member([user_id1], user_id1))
        self.assertFalse(checker.is_member([user_id1], user_id2))

    def test_user_in_group(self) -> None:
        group_id1 = uuid.uuid4()
        group_id2 = uuid.uuid4()
        user_id = uuid.uuid4()
        checker: GroupMembershipChecker = StaticGroupMembership(
            {str(user_id): [group_id1]}
        )
        self.assertTrue(checker.is_member([group_id1], user_id))
        self.assertFalse(checker.is_member([group_id2], user_id))
