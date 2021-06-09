#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.


import unittest
from uuid import uuid4

from onefuzztypes.enums import ContainerType, TaskType
from onefuzztypes.events import EventTaskCreated
from onefuzztypes.models import (
    TaskConfig,
    TaskContainers,
    TaskDetails,
    TaskPool,
    UserInfo,
)
from onefuzztypes.primitives import Container, PoolName

from __app__.onefuzzlib.events import filter_event


class TestUserInfoFilter(unittest.TestCase):
    def test_user_info_filter(self) -> None:
        job_id = uuid4()
        task_id = uuid4()
        application_id = uuid4()
        object_id = uuid4()
        upn = "testalias@contoso.com"

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

        scrubbed_test_event = filter_event(test_event)

        self.assertEqual(scrubbed_test_event, control_test_event)


if __name__ == "__main__":
    unittest.main()
