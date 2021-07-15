from typing import Dict, List
from uuid import UUID
import uuid


class Permissions:
    allowed_groups_ids: List[UUID]
    allowed_members_ids: List[UUID]

    def __init__(self, allowed_groups_ids: List[UUID] = [], allowed_members_ids: List[UUID] = []) -> None:
        self.allowed_groups_ids = allowed_groups_ids
        self.allowed_members_ids = allowed_members_ids

class RequestTrieNode:
    permissions: Permissions
    children: Dict[str, "RequestTrieNode"]

    def __init__(self) -> None:
        self.permissions = Permissions()
        self.children = {}
        pass


class RequestTrie:
    root: RequestTrieNode

    def __init__(self) -> None:
        self.root = RequestTrieNode()

    def add_url(
        self, path: str, permissions: Permissions
    ):
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
            current_node.children[current_segment] = RequestTrieNode()
            current_node = current_node.children[current_segment]
            current_segment_index = current_segment_index+1

        current_node.permissions = permissions


    def get_matching_rules(self, path: str) -> Permissions:
        segments = path.split("/")
        if len(segments) == 0:
            return Permissions()

        current_node = self.root
        current_segment_index = 0

        while current_segment_index < len(segments):
            current_segment = segments[current_segment_index]
            if (current_segment in current_node.children) or (
                "*" in current_node.children
            ):
                current_node = current_node.children[current_segment]
                current_segment_index = current_segment_index + 1
            else:
                break

        return current_node.permissions




import unittest
class TestRequestTrie(unittest.TestCase):
    def test(self) -> None:

        guid1 = uuid.uuid4()
        guid2 = uuid.uuid4()

        request_trie = RequestTrie()
        request_trie.add_url("a/b/c", Permissions(allowed_groups_ids=[guid1]))
        request_trie.add_url("b/*/c", Permissions(allowed_groups_ids=[guid2]))

        permissions = request_trie.get_matching_rules("a/b/c")
        self.assertNotEqual(len(permissions.allowed_groups_ids), 0, "empty allowed groups")
        self.assertEqual(permissions.allowed_groups_ids[0], guid1)


        # permission



