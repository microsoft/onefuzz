from typing import Dict, List, Optional
from uuid import UUID

from memoization.memoization import cached


class RequestAuthorization:
    """
    Stores the rules associated with a the request paths
    """

    class Rules:
        allowed_groups_ids: List[UUID]

        def __init__(self, allowed_groups_ids: List[UUID] = []) -> None:
            self.allowed_groups_ids = allowed_groups_ids

    class Node:
        # http method -> rules
        rules: Dict[str, "RequestAuthorization.Rules"]
        # path -> node
        children: Dict[str, "RequestAuthorization.Node"]

        def __init__(self) -> None:
            self.rules = {}
            self.children = {}
            pass

    root: Node

    def __init__(self) -> None:
        self.root = RequestAuthorization.Node()

    def add_url(self, methods: List[str], path: str, rules: Rules) -> None:
        methods = map(lambda m: m.upper(), methods)
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
            for method in methods:
                if method in current_node.rules:
                    raise Exception(f"Conflicting rules on {method} {path}")

        while current_segment_index < len(segments):
            current_segment = segments[current_segment_index]
            current_node.children[current_segment] = RequestAuthorization.Node()
            current_node = current_node.children[current_segment]
            current_segment_index = current_segment_index + 1

        for method in methods:
            current_node.rules[method] = rules

    @cached
    def get_matching_rules(self, method: str, path: str) -> Rules:
        method = method.upper()
        segments = path.split("/")
        current_node = self.root

        if method in current_node.rules:
            current_rule = current_node.rules[method]
        else:
            current_rule = RequestAuthorization.Rules()

        current_segment_index = 0

        while current_segment_index < len(segments):
            current_segment = segments[current_segment_index]
            if current_segment in current_node.children:
                current_node = current_node.children[current_segment]
            elif "*" in current_node.children:
                current_node = current_node.children["*"]
            else:
                break

            if method in current_node.rules:
                current_rule = current_node.rules[method]
            current_segment_index = current_segment_index + 1

        return current_rule
