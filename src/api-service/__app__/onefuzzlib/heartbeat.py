#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from datetime import datetime, timedelta
from typing import Dict, List, Tuple
from uuid import UUID

from onefuzztypes.enums import HeartbeatType
from onefuzztypes.models import NodeHeartbeat as BASE_NODE_HEARTBEAT
from onefuzztypes.models import NodeHeartbeatEntry, NodeHeartbeatSummary
from onefuzztypes.models import TaskHeartbeat as BASE_TASK_HEARTBEAT
from onefuzztypes.models import TaskHeartbeatEntry, TaskHeartbeatSummary
from pydantic import ValidationError

from .orm import ORMMixin


class TaskHeartbeat(BASE_TASK_HEARTBEAT, ORMMixin):
    @classmethod
    def add(cls, entry: TaskHeartbeatEntry) -> None:
        for value in entry.data:
            heartbeat_id = "-".join([str(entry.machine_id), value["type"].name])
            heartbeat = cls(
                task_id=entry.task_id,
                heartbeat_id=heartbeat_id,
                machine_id=entry.machine_id,
                heartbeat_type=value["type"],
            )
            heartbeat.save()

    @classmethod
    def try_add(cls, raw: Dict) -> bool:
        try:
            entry = TaskHeartbeatEntry.parse_obj(raw)
            cls.add(entry)
            return True
        except ValidationError:
            return False

    @classmethod
    def get_heartbeats(cls, task_id: UUID) -> List[TaskHeartbeatSummary]:
        entries = cls.search(query={"task_id": [task_id]})

        result = []
        for entry in entries:
            result.append(
                TaskHeartbeatSummary(
                    timestamp=entry.Timestamp,
                    machine_id=entry.machine_id,
                    type=entry.heartbeat_type,
                )
            )
        return result

    @classmethod
    def key_fields(cls) -> Tuple[str, str]:
        return ("task_id", "heartbeat_id")


class NodeHeartbeat(BASE_NODE_HEARTBEAT, ORMMixin):
    @classmethod
    def get_heartbeat_id(cls, node_id: UUID, heartbeat_type: HeartbeatType):
        return "-".join([str(node_id), heartbeat_type.name])

    @classmethod
    def add(cls, entry: NodeHeartbeatEntry) -> None:
        for value in entry.data:
            heartbeat_id = cls.get_heartbeat_id(entry.node_id, value["type"].name)
            heartbeat = cls(
                heartbeat_id=heartbeat_id,
                node_id=entry.node_id,
                heartbeat_type=value["type"],
            )
            heartbeat.save()

    @classmethod
    def try_add(cls, raw: Dict) -> bool:
        try:
            entry = NodeHeartbeatEntry.parse_obj(raw)
            cls.add(entry)
            return True
        except ValidationError:
            return False

    @classmethod
    def get_heartbeats(cls, node_id: UUID) -> List[NodeHeartbeatSummary]:
        entries = cls.search(query={"node_id": [node_id]})

        result = []
        for entry in entries:
            result.append(
                NodeHeartbeatSummary(
                    timestamp=entry.Timestamp,
                    type=entry.heartbeat_type,
                )
            )
        return result

    @classmethod
    def get_dead_nodes(
        cls, node_ids: List[UUID], expiration_period: timedelta
    ) -> List[UUID]:
        time_filter = "Timestamp lt datetime'%s'" % (
            datetime.utcnow().isoformat() - expiration_period
        )
        entries = cls.search(
            query={
                "heartbeat_id": [
                    cls.get_heartbeat_id(n, HeartbeatType.MachineAlive)
                    for n in node_ids
                ]
            },
            raw_unchecked_filter=time_filter,
        )
        return [e.node_id for e in entries]

    @classmethod
    def key_fields(cls) -> Tuple[str, str]:
        return ("node_id", "heartbeat_id")
