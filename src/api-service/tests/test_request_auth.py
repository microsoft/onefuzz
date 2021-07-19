import unittest
import uuid

from __app__.onefuzzlib.request_auth import RequestAuthorization


class TestRequestAuthorization(unittest.TestCase):
    def test_exact_match(self) -> None:

        guid1 = uuid.uuid4()

        request_trie = RequestAuthorization()
        request_trie.add_url(
            "a/b/c", RequestAuthorization.Rules(allowed_groups_ids=[guid1])
        )

        rules1 = request_trie.get_matching_rules("a/b/c")
        rules2 = request_trie.get_matching_rules("b/b/e")

        self.assertNotEqual(len(rules1.allowed_groups_ids), 0, "empty allowed groups")
        self.assertEqual(rules1.allowed_groups_ids[0], guid1)

        self.assertEqual(len(rules2.allowed_groups_ids), 0, "expected nothing")

    def test_wildcard(self) -> None:
        guid1 = uuid.uuid4()

        request_trie = RequestAuthorization()
        request_trie.add_url(
            "b/*/c", RequestAuthorization.Rules(allowed_groups_ids=[guid1])
        )

        rules = request_trie.get_matching_rules("b/b/c")

        self.assertNotEqual(len(rules.allowed_groups_ids), 0, "empty allowed groups")
        self.assertEqual(rules.allowed_groups_ids[0], guid1)

    def test_adding_rule_on_same_path(self) -> None:
        guid1 = uuid.uuid4()

        request_trie = RequestAuthorization()
        request_trie.add_url(
            "a/b/c", RequestAuthorization.Rules(allowed_groups_ids=[guid1])
        )

        try:
            request_trie.add_url(
                "a/b/c", RequestAuthorization.Rules(allowed_groups_ids=[])
            )
            self.fail("this is expected to fail")
        except Exception:
            pass

    # The most specific rules takes priority over the ones containing a wildcard
    def test_priority(self) -> None:
        guid1 = uuid.uuid4()
        guid2 = uuid.uuid4()

        request_trie = RequestAuthorization()
        request_trie.add_url(
            "a/*/c", RequestAuthorization.Rules(allowed_groups_ids=[guid1])
        )

        request_trie.add_url(
            "a/b/c", RequestAuthorization.Rules(allowed_groups_ids=[guid2])
        )

        rules = request_trie.get_matching_rules("a/b/c")

        self.assertEqual(
            rules.allowed_groups_ids[0],
            guid2,
            "expected to match the most specific rule",
        )

    # if a path has no specific rule. it inherits from the parents
    # /a/b/c inherint from a/b
    # todo test the wildcard behavior
    def test_inherit_rule(self) -> None:
        guid1 = uuid.uuid4()
        guid2 = uuid.uuid4()

        request_trie = RequestAuthorization()
        request_trie.add_url(
            "a/b/c", RequestAuthorization.Rules(allowed_groups_ids=[guid1])
        )

        request_trie.add_url(
            "f/*/c", RequestAuthorization.Rules(allowed_groups_ids=[guid2])
        )

        rules = request_trie.get_matching_rules("a/b/c/d")
        self.assertEqual(
            rules.allowed_groups_ids[0], guid1, "expected to inherit rule of a/b/c"
        )

        rules = request_trie.get_matching_rules("f/b/c/d")
        self.assertEqual(
            rules.allowed_groups_ids[0], guid2, "expected to inherit rule of f/*/c"
        )

    # the lowest level rule override the parent rules
    def test_override_rule(self) -> None:
        guid1 = uuid.uuid4()
        guid2 = uuid.uuid4()

        request_trie = RequestAuthorization()
        request_trie.add_url(
            "a/b/c", RequestAuthorization.Rules(allowed_groups_ids=[guid1])
        )

        request_trie.add_url(
            "a/b/c/d", RequestAuthorization.Rules(allowed_groups_ids=[guid2])
        )

        rules = request_trie.get_matching_rules("a/b/c")
        self.assertEqual(
            rules.allowed_groups_ids[0], guid1, "expected to inherit rule of a/b/c"
        )

        rules = request_trie.get_matching_rules("a/b/c/d")
        self.assertEqual(
            rules.allowed_groups_ids[0], guid2, "expected to inherit rule of a/b/c/d"
        )
