#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import unittest
from typing import Dict, Generator, List
from uuid import UUID, uuid4

from onefuzztypes.enums import OS, ContainerType, TaskType
from onefuzztypes.models import TaskConfig, TaskContainers, TaskDetails, TaskPool
from onefuzztypes.primitives import Container, PoolName

from __app__.onefuzzlib.tasks.main import Task
from __app__.onefuzzlib.tasks.scheduler import bucket_tasks


def chunks(items: List, size: int) -> Generator:
    return (items[x : x + size] for x in range(0, len(items), size))


class TestTaskBuckets(unittest.TestCase):
    def build_tasks(self, size: int) -> List[Task]:
        tasks = []
        for _ in range(size):
            task = Task(
                job_id=UUID(int=0),
                config=TaskConfig(
                    job_id=UUID(int=0),
                    task=TaskDetails(
                        type=TaskType.libfuzzer_fuzz,
                        duration=1,
                        target_exe="fuzz.exe",
                        target_env={},
                        target_options=[],
                    ),
                    pool=TaskPool(pool_name=PoolName("pool"), count=1),
                    containers=[
                        TaskContainers(
                            type=ContainerType.setup, name=Container("setup")
                        )
                    ],
                    tags={},
                    colocate=True,
                ),
                os=OS.linux,
            )
            tasks.append(task)
        return tasks

    def test_all_colocate(self) -> None:
        # all tasks should land in one bucket
        tasks = self.build_tasks(10)
        for task in tasks:
            task.config.colocate = True

        buckets = bucket_tasks(tasks)

        for bucket in buckets.values():
            self.assertEqual(len(bucket), 10)

        self.check_buckets(buckets, tasks, bucket_count=1)

    def test_partial_colocate(self) -> None:
        # 2 tasks should land on their own, the rest should be colocated into a
        # single bucket.

        tasks = self.build_tasks(10)

        # a the task came before colocation was defined
        tasks[0].config.colocate = None

        # a the task shouldn't be colocated
        tasks[1].config.colocate = False

        buckets = bucket_tasks(tasks)

        lengths = []
        for bucket in buckets.values():
            lengths.append(len(bucket))
        self.assertEqual([1, 1, 8], sorted(lengths))
        self.check_buckets(buckets, tasks, bucket_count=3)

    def test_all_unique_job(self) -> None:
        # everything has a unique job_id
        tasks = self.build_tasks(10)
        for task in tasks:
            job_id = uuid4()
            task.job_id = job_id
            task.config.job_id = job_id

        buckets = bucket_tasks(tasks)

        for bucket in buckets.values():
            self.assertEqual(len(bucket), 1)

        self.check_buckets(buckets, tasks, bucket_count=10)

    def test_multiple_job_buckets(self) -> None:
        # at most 3 tasks per bucket, by job_id
        tasks = self.build_tasks(10)
        for task_chunks in chunks(tasks, 3):
            job_id = uuid4()
            for task in task_chunks:
                task.job_id = job_id
                task.config.job_id = job_id

        buckets = bucket_tasks(tasks)

        for bucket in buckets.values():
            self.assertLessEqual(len(bucket), 3)

        self.check_buckets(buckets, tasks, bucket_count=4)

    def test_many_buckets(self) -> None:
        tasks = self.build_tasks(100)
        job_id = UUID(int=1)
        for i, task in enumerate(tasks):
            if i % 2 == 0:
                task.job_id = job_id
                task.config.job_id = job_id

            if i % 3 == 0:
                task.os = OS.windows

            if i % 4 == 0:
                task.config.containers[0].name = Container("setup2")

            if i % 5 == 0:
                if task.config.pool:
                    task.config.pool.pool_name = PoolName("alternate-pool")

        buckets = bucket_tasks(tasks)
        self.check_buckets(buckets, tasks, bucket_count=12)

    def check_buckets(self, buckets: Dict, tasks: List, *, bucket_count: int) -> None:
        self.assertEqual(len(buckets), bucket_count, "bucket count")

        for task in tasks:
            seen = False
            for bucket in buckets.values():
                if task in bucket:
                    self.assertEqual(seen, False, "task seen in multiple buckets")
                    seen = True
            self.assertEqual(seen, True, "task not seein in any buckets")
