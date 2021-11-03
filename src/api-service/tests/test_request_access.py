import unittest
import uuid

from onefuzztypes.models import ApiAccessRule

from __app__.onefuzzlib.request_access import RequestAccess, RuleConflictError


class TestRequestAccess(unittest.TestCase):
    def test_empty(self) -> None:
        request_access1 = RequestAccess.build([])
        rules1 = request_access1.get_matching_rules("get", "a/b/c")

        self.assertEqual(len(rules1.allowed_groups_ids), 0, "expected nothing")

        guid2 = uuid.uuid4()
        request_access1 = RequestAccess.build(
            [
                ApiAccessRule(
                    methods=["get"],
                    endpoint="a/b/c",
                    allowed_groups=[guid2],
                )
            ]
        )
        rules1 = request_access1.get_matching_rules("get", "")
        self.assertEqual(len(rules1.allowed_groups_ids), 0, "expected nothing")

    def test_exact_match(self) -> None:

        guid1 = uuid.uuid4()

        request_access = RequestAccess.build(
            [
                ApiAccessRule(
                    methods=["get"],
                    endpoint="a/b/c",
                    allowed_groups=[guid1],
                )
            ]
        )

        rules1 = request_access.get_matching_rules("get", "a/b/c")
        rules2 = request_access.get_matching_rules("get", "b/b/e")

        self.assertNotEqual(len(rules1.allowed_groups_ids), 0, "empty allowed groups")
        self.assertEqual(rules1.allowed_groups_ids[0], guid1)

        self.assertEqual(len(rules2.allowed_groups_ids), 0, "expected nothing")

    def test_wildcard(self) -> None:
        guid1 = uuid.uuid4()

        request_access = RequestAccess.build(
            [
                ApiAccessRule(
                    methods=["get"],
                    endpoint="b/*/c",
                    allowed_groups=[guid1],
                )
            ]
        )

        rules = request_access.get_matching_rules("get", "b/b/c")

        self.assertNotEqual(len(rules.allowed_groups_ids), 0, "empty allowed groups")
        self.assertEqual(rules.allowed_groups_ids[0], guid1)

    def test_adding_rule_on_same_path(self) -> None:
        guid1 = uuid.uuid4()

        try:
            RequestAccess.build(
                [
                    ApiAccessRule(
                        methods=["get"],
                        endpoint="a/b/c",
                        allowed_groups=[guid1],
                    ),
                    ApiAccessRule(
                        methods=["get"],
                        endpoint="a/b/c",
                        allowed_groups=[],
                    ),
                ]
            )

            self.fail("this is expected to fail")
        except RuleConflictError:
            pass

    # The most specific rules takes priority over the ones containing a wildcard
    def test_priority(self) -> None:
        guid1 = uuid.uuid4()
        guid2 = uuid.uuid4()

        request_access = RequestAccess.build(
            [
                ApiAccessRule(
                    methods=["get"],
                    endpoint="a/*/c",
                    allowed_groups=[guid1],
                ),
                ApiAccessRule(
                    methods=["get"],
                    endpoint="a/b/c",
                    allowed_groups=[guid2],
                ),
            ]
        )

        rules = request_access.get_matching_rules("get", "a/b/c")

        self.assertEqual(
            rules.allowed_groups_ids[0],
            guid2,
            "expected to match the most specific rule",
        )

    # if a path has no specific rule. it inherits from the parents
    # /a/b/c inherit from a/b
    def test_inherit_rule(self) -> None:
        guid1 = uuid.uuid4()
        guid2 = uuid.uuid4()
        guid3 = uuid.uuid4()

        request_access = RequestAccess.build(
            [
                ApiAccessRule(
                    methods=["get"],
                    endpoint="a/b/c",
                    allowed_groups=[guid1],
                ),
                ApiAccessRule(
                    methods=["get"],
                    endpoint="f/*/c",
                    allowed_groups=[guid2],
                ),
                ApiAccessRule(
                    methods=["post"],
                    endpoint="a/b",
                    allowed_groups=[guid3],
                ),
            ]
        )

        rules1 = request_access.get_matching_rules("get", "a/b/c/d")
        self.assertEqual(
            rules1.allowed_groups_ids[0], guid1, "expected to inherit rule of a/b/c"
        )

        rules2 = request_access.get_matching_rules("get", "f/b/c/d")
        self.assertEqual(
            rules2.allowed_groups_ids[0], guid2, "expected to inherit rule of f/*/c"
        )

        rules3 = request_access.get_matching_rules("post", "a/b/c/d")
        self.assertEqual(
            rules3.allowed_groups_ids[0], guid3, "expected to inherit rule of post a/b"
        )

    # the lowest level rule override the parent rules
    def test_override_rule(self) -> None:
        guid1 = uuid.uuid4()
        guid2 = uuid.uuid4()

        request_access = RequestAccess.build(
            [
                ApiAccessRule(
                    methods=["get"],
                    endpoint="a/b/c",
                    allowed_groups=[guid1],
                ),
                ApiAccessRule(
                    methods=["get"],
                    endpoint="a/b/c/d",
                    allowed_groups=[guid2],
                ),
            ]
        )

        rules1 = request_access.get_matching_rules("get", "a/b/c")
        self.assertEqual(
            rules1.allowed_groups_ids[0], guid1, "expected to inherit rule of a/b/c"
        )

        rules2 = request_access.get_matching_rules("get", "a/b/c/d")
        self.assertEqual(
            rules2.allowed_groups_ids[0], guid2, "expected to inherit rule of a/b/c/d"
        )
