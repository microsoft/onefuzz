#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.


import unittest
from uuid import uuid4

from onefuzztypes.enums import ContainerType, TaskType
from onefuzztypes.models import (
    TaskConfig,
    TaskContainers,
    TaskDetails,
    TaskPool,
    UserInfo,
)
from onefuzztypes.events import get_event_type, EventTaskCreated
from onefuzztypes.primitives import Container, PoolName

from __app__.onefuzzlib.events import log_event


class TestSecretFilter(unittest.TestCase):
    def test_secret_filter(self) -> None:
        job_id = uuid4()
        task_id = uuid4()
        application_id = uuid4()
        object_id = uuid4()
        upn = "testalias@microsoft.com"

        user_info = UserInfo(
            application_id=application_id, object_id=object_id, upn=upn
        )

        task_config = TaskConfig(
            job_id=job_id,
            containers=[
                TaskContainers(
                    type=ContainerType.inputs, name=Container("test-container")
                )
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

        control_test_event = EventTaskCreated(
            job_id=job_id,
            task_id=task_id,
            config=task_config,
            user_info=None,
        )

        test_event_type = get_event_type(test_event)

        scrubbed_test_event = log_event(test_event, test_event_type)

        self.assertEqual(scrubbed_test_event, control_test_event)


if __name__ == "__main__":
    unittest.main()
