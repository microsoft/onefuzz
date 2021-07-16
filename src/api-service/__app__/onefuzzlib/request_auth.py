import uuid
from typing import Dict, List, Optional
from uuid import UUID


class RequestAuthorization:
    class Rules:
        allowed_groups_ids: List[UUID]

        def __init__(self, allowed_groups_ids: List[UUID] = []) -> None:
            self.allowed_groups_ids = allowed_groups_ids

    class Node:
        rules: "RequestAuthorization.Rules"
        children: Dict[str, "RequestAuthorization.Node"]

        def __init__(self) -> None:
            self.rules = RequestAuthorization.Rules()
            self.children = {}
            pass

    root: Node

    def __init__(self) -> None:
        self.root = RequestAuthorization.Node()

    def add_url(self, path: str, rules: Rules) -> None:
        segments = path.split("/")
        if len(segments) == 0:
            return

        current_node = self.root
        current_segment_index = 0

        while current_segment_index < len(segments):
            current_segment = segments[current_segment_index]
            if current_segment in current_node.children:
                current_node = current_node.children[current_segment]
                current_segment_index = current_segment_index + 1
            else:
                break

        # we found a node matching this exact path
        # This means that there is an existing rule causing a conflict
        if current_segment_index == len(segments):
            raise Exception(f"Conflicting path {path}")

        while current_segment_index < len(segments):
            current_segment = segments[current_segment_index]
            current_node.children[current_segment] = RequestAuthorization.Node()
            current_node = current_node.children[current_segment]
            current_segment_index = current_segment_index + 1

        current_node.rules = rules

    def get_matching_rules(self, path: str) -> Rules:
        segments = path.split("/")
        current_node = self.root
        current_segment_index = 0

        while current_segment_index < len(segments):
            current_segment = segments[current_segment_index]
            if current_segment in current_node.children:
                current_node = current_node.children[current_segment]
            elif "*" in current_node.children:
                current_node = current_node.children["*"]
            else:
                break

            current_segment_index = current_segment_index + 1

        return current_node.rules


import unittest


class TestRequestAuthorization(unittest.TestCase):
    def test(self) -> None:

        guid1 = uuid.uuid4()
        guid2 = uuid.uuid4()

        request_trie = RequestAuthorization()
        request_trie.add_url(
            "a/b/c", RequestAuthorization.Rules(allowed_groups_ids=[guid1])
        )
        request_trie.add_url(
            "b/*/c", RequestAuthorization.Rules(allowed_groups_ids=[guid2])
        )

        rules = request_trie.get_matching_rules("a/b/c")

        self.assertNotEqual(
            len(rules.allowed_groups_ids), 0, "empty allowed groups"
        )
        self.assertEqual(rules.allowed_groups_ids[0], guid1)

        # permission
