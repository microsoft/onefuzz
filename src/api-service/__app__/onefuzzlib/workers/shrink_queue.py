#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from uuid import UUID, uuid4

from pydantic import BaseModel, Field

from ..azure.queue import (
    clear_queue,
    create_queue,
    delete_queue,
    queue_object,
    remove_first_message,
)
from ..azure.storage import StorageType


class ShrinkEntry(BaseModel):
    shrink_id: UUID = Field(default_factory=uuid4)


class ShrinkQueue:
    def __init__(self, base_id: UUID):
        self.base_id = base_id

    def queue_name(self) -> str:
        return "to-shrink-%s" % self.base_id.hex

    def clear(self) -> None:
        clear_queue(self.queue_name(), StorageType.config)

    def create(self) -> None:
        create_queue(self.queue_name(), StorageType.config)

    def delete(self) -> None:
        delete_queue(self.queue_name(), StorageType.config)

    def add_entry(self) -> None:
        queue_object(self.queue_name(), ShrinkEntry(), StorageType.config)

    def set_size(self, size: int) -> None:
        self.clear()
        for _ in range(size):
            self.add_entry()

    def should_shrink(self) -> bool:
        return remove_first_message(self.queue_name(), StorageType.config)
