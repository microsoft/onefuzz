import unittest
import uuid

from __app__.onefuzzlib.request_auth import RequestAuthorization


class TestRequestAuthorization(unittest.TestCase):
    def test_exact_match(self) -> None:

        guid1 = uuid.uuid4()
        guid2 = uuid.uuid4()

        request_trie = RequestAuthorization()
        request_trie.add_url(
            "a/b/c", RequestAuthorization.Rules(allowed_groups_ids=[guid1])
        )

        rules1 = request_trie.get_matching_rules("a/b/c")
        rules2 = request_trie.get_matching_rules("b/b/e")

        self.assertNotEqual(
            len(rules1.allowed_groups_ids), 0, "empty allowed groups"
        )
        self.assertEqual(rules1.allowed_groups_ids[0], guid1)


        self.assertEqual(
            len(rules2.allowed_groups_ids), 0, "expected nothing"
        )


    def test_wildcard(self):
        guid1 = uuid.uuid4()

        request_trie = RequestAuthorization()
        request_trie.add_url(
            "b/*/c", RequestAuthorization.Rules(allowed_groups_ids=[guid1])
        )

        rules = request_trie.get_matching_rules("b/b/c")

        self.assertNotEqual(
            len(rules.allowed_groups_ids), 0, "empty allowed groups"
        )
        self.assertEqual(rules.allowed_groups_ids[0], guid1)
