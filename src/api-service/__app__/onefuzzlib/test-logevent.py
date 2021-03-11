#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
import logging
from queue import Empty, Queue
from typing import Dict, List, Set
from uuid import UUID, uuid4

from onefuzztypes.enums import OS, Architecture, ContainerType, TaskType
from onefuzztypes.events import (Event, EventMessage, EventTaskCreated,
                                 EventType, get_event_type)
from onefuzztypes.models import (SecretData, TaskConfig, TaskContainers,
                                 TaskDetails, TaskPool, UserInfo)
from onefuzztypes.primitives import Container, PoolName
from pydantic import BaseModel

EVENTS: Queue = Queue()


def log_event(event: Event, event_type: EventType) -> None:

    clone_event = event.copy(deep=True)
    log_event_recurs(clone_event)
    print(clone_event.json(indent=2))
    logging.info("sending event: %s - %s", event_type, clone_event)


def log_event_recurs(clone_event: Event, visited: Set[int] = set()) -> Event:

    if id(clone_event) in visited:
        return

    visited.add(id(clone_event))

    for field in clone_event.__fields__:
        field_data = getattr(clone_event, field)

        if isinstance(field_data, UserInfo):

            field_data = None

        elif isinstance(field_data, List):

            if len(field_data) > 0 and not isinstance(field_data[0], BaseModel):
                continue
            for data in field_data:
                log_event_recurs(data, visited)

        elif isinstance(field_data, dict):

            for key in field_data:
                if not isinstance(field_data[key], BaseModel):
                    continue
                log_event_recurs(field_data[key], visited)

        else:

            if isinstance(field_data, BaseModel):
                log_event_recurs(field_data, visited)

        setattr(clone_event, field, field_data)

    return clone_event


if __name__ == "__main__":

    job_id = uuid4()
    task_id = uuid4()
    application_id = uuid4()
    object_id = uuid4()
    upn = "noharper@microsoft.com"

    user_info = UserInfo(application_id=application_id, object_id=object_id, upn=upn)

    task_config = TaskConfig(
        job_id=job_id,
        containers=[
            TaskContainers(type=ContainerType.inputs, name=Container("test-container"))
        ],
        tags={},
        task=TaskDetails(
            type=TaskType.libfuzzer_fuzz,
            duration=12,
            target_exe="fuzz.exe",
            target_env={},
            target_options=[],
        ),
        pool=TaskPool(count=2, pool_name=PoolName("test-pool")),
    )

    test_event = EventTaskCreated(
        job_id=job_id,
        task_id=task_id,
        config=task_config,
        user_info=user_info,
    )

    test_event_type = get_event_type(test_event)
    # print(test_event_type)
    log_event(test_event, test_event_type)
    print(test_event.json(indent=2))
