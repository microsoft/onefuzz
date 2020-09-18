#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from typing import List, Tuple
from uuid import UUID

from onefuzztypes.models import Heartbeat as BASE
from onefuzztypes.models import HeartbeatEntry, HeartbeatSummary

from .orm import ORMMixin


class Heartbeat(BASE, ORMMixin):
    @classmethod
    def add(cls, entry: HeartbeatEntry) -> None:
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
    def get_heartbeats(cls, task_id: UUID) -> List[HeartbeatSummary]:
        entries = cls.search(query={"task_id": [task_id]})

        result = []
        for entry in entries:
            result.append(
                HeartbeatSummary(
                    timestamp=entry.Timestamp,
                    machine_id=entry.machine_id,
                    type=entry.heartbeat_type,
                )
            )
        return result

    @classmethod
    def key_fields(cls) -> Tuple[str, str]:
        return ("task_id", "heartbeat_id")
