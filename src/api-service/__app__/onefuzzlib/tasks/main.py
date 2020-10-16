#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
import logging
from datetime import datetime, timedelta
from typing import Dict, List, Optional, Tuple, Union
from uuid import UUID

from onefuzztypes.enums import ErrorCode, TaskState
from onefuzztypes.models import Error
from onefuzztypes.models import Task as BASE_TASK
from onefuzztypes.models import TaskConfig, TaskHeartbeat, TaskHeartbeatEntry, TaskVm
from pydantic import ValidationError

from ..azure.creds import get_fuzz_storage
from ..azure.image import get_os
from ..azure.queue import create_queue, delete_queue
from ..orm import MappingIntStrAny, ORMMixin, QueryFilter
from ..pools import Node, Pool, Scaleset
from ..proxy_forward import ProxyForward


class Task(BASE_TASK, ORMMixin):
    def ready_to_schedule(self) -> bool:
        if self.config.prereq_tasks:
            for task_id in self.config.prereq_tasks:
                task = Task.get_by_task_id(task_id)
                # if a prereq task fails, then mark this task as failed
                if isinstance(task, Error):
                    self.error = task
                    self.state = TaskState.stopping
                    self.save()
                    return False

                if task.state not in task.state.has_started():
                    return False
        return True

    @classmethod
    def create(cls, config: TaskConfig, job_id: UUID) -> Union["Task", Error]:
        if config.vm:
            os = get_os(config.vm.region, config.vm.image)
        elif config.pool:
            pool = Pool.get_by_name(config.pool.pool_name)
            if isinstance(pool, Error):
                return pool
            os = pool.os
        else:
            raise Exception("task must have vm or pool")
        task = cls(config=config, job_id=job_id, os=os)
        task.save()
        return task

    def save_exclude(self) -> Optional[MappingIntStrAny]:
        return {"heartbeats": ...}

    def is_ready(self) -> bool:
        if self.config.prereq_tasks:
            for prereq_id in self.config.prereq_tasks:
                prereq = Task.get_by_task_id(prereq_id)
                if isinstance(prereq, Error):
                    logging.info("task prereq has error: %s - %s", self.task_id, prereq)
                    self.error = prereq
                    self.state = TaskState.stopping
                    self.save()
                    return False
                if prereq.state != TaskState.running:
                    logging.info(
                        "task is waiting on prereq: %s - %s:",
                        self.task_id,
                        prereq.task_id,
                    )
                    return False

        return True

    # At current, the telemetry filter will generate something like this:
    #
    # {
    #     'job_id': 'f4a20fd8-0dcc-4a4e-8804-6ef7df50c978',
    #     'task_id': '835f7b3f-43ad-4718-b7e4-d506d9667b09',
    #     'state': 'stopped',
    #     'config': {
    #         'task': {'type': 'libfuzzer_coverage'},
    #         'vm': {'count': 1}
    #     }
    # }
    def telemetry_include(self) -> Optional[MappingIntStrAny]:
        return {
            "job_id": ...,
            "task_id": ...,
            "state": ...,
            "config": {"vm": {"count": ...}, "task": {"type": ...}},
        }

    def event_include(self) -> Optional[MappingIntStrAny]:
        return {
            "job_id": ...,
            "task_id": ...,
            "state": ...,
            "error": ...,
        }

    def init(self) -> None:
        create_queue(self.task_id, account_id=get_fuzz_storage())
        self.state = TaskState.waiting
        self.save()

    def stopping(self) -> None:
        # TODO: we need to 'unschedule' this task from the existing pools

        self.state = TaskState.stopping
        logging.info("stopping task: %s:%s", self.job_id, self.task_id)
        ProxyForward.remove_forward(self.task_id)
        delete_queue(str(self.task_id), account_id=get_fuzz_storage())
        Node.stop_task(self.task_id)
        self.state = TaskState.stopped
        self.save()

    def queue_stop(self) -> None:
        self.queue(method=self.stopping)

    @classmethod
    def search_states(
        cls, *, job_id: Optional[UUID] = None, states: Optional[List[TaskState]] = None
    ) -> List["Task"]:
        query: QueryFilter = {}
        if job_id:
            query["job_id"] = [job_id]
        if states:
            query["state"] = states

        return cls.search(query=query)

    @classmethod
    def search_expired(cls) -> List["Task"]:
        time_filter = "end_time lt datetime'%s'" % datetime.utcnow().isoformat()
        return cls.search(
            query={"state": TaskState.available()}, raw_unchecked_filter=time_filter
        )

    @classmethod
    def try_get_by_task_id(cls, task_id: UUID) -> Optional["Task"]:
        tasks = cls.search(query={"task_id": [task_id]})
        if not tasks:
            return None
        return tasks[0]

    @classmethod
    def get_by_task_id(cls, task_id: UUID) -> Union[Error, "Task"]:
        tasks = cls.search(query={"task_id": [task_id]})
        if not tasks:
            return Error(code=ErrorCode.INVALID_REQUEST, errors=["unable to find task"])

        if len(tasks) != 1:
            return Error(
                code=ErrorCode.INVALID_REQUEST, errors=["error identifying task"]
            )
        task = tasks[0]
        return task

    @classmethod
    def get_tasks_by_pool_name(cls, pool_name: str) -> List["Task"]:
        tasks = cls.search_states(states=TaskState.available())
        if not tasks:
            return []

        pool_tasks = []

        for task in tasks:
            task_pool = task.get_pool()
            if not task_pool:
                continue
            if pool_name == task_pool.name and task.state in TaskState.available():
                pool_tasks.append(task)

        return pool_tasks

    def mark_stopping(self) -> None:
        if self.state not in [TaskState.stopped, TaskState.stopping]:
            self.state = TaskState.stopping
            self.save()

    def mark_failed(self, error: Error) -> None:
        if self.state in [TaskState.stopped, TaskState.stopping]:
            logging.debug(
                "ignoring post-task stop failures for %s:%s", self.job_id, self.task_id
            )
            return

        self.error = error
        self.state = TaskState.stopping
        self.save()

    def get_pool(self) -> Optional[Pool]:
        if self.config.pool:
            pool = Pool.get_by_name(self.config.pool.pool_name)
            if isinstance(pool, Error):
                logging.info(
                    "unable to schedule task to pool: %s - %s", self.task_id, pool
                )
                return None
            return pool
        elif self.config.vm:
            scalesets = Scaleset.search()
            scalesets = [
                x
                for x in scalesets
                if x.vm_sku == self.config.vm.sku and x.image == self.config.vm.image
            ]
            for scaleset in scalesets:
                pool = Pool.get_by_name(scaleset.pool_name)
                if isinstance(pool, Error):
                    logging.info(
                        "unable to schedule task to pool: %s - %s",
                        self.task_id,
                        pool,
                    )
                else:
                    return pool

        logging.warning(
            "unable to find a scaleset that matches the task prereqs: %s",
            self.task_id,
        )
        return None

    def get_repro_vm_config(self) -> Union[TaskVm, None]:
        if self.config.vm:
            return self.config.vm

        if self.config.pool is None:
            raise Exception("either pool or vm must be specified: %s" % self.task_id)

        pool = Pool.get_by_name(self.config.pool.pool_name)
        if isinstance(pool, Error):
            logging.info("unable to find pool from task: %s", self.task_id)
            return None

        for scaleset in Scaleset.search_by_pool(self.config.pool.pool_name):
            return TaskVm(
                region=scaleset.region,
                sku=scaleset.vm_sku,
                image=scaleset.image,
            )

        logging.warning(
            "no scalesets are defined for task: %s:%s", self.job_id, self.task_id
        )
        return None

    def on_start(self) -> None:
        # try to keep this effectively idempotent

        if self.end_time is None:
            self.end_time = datetime.utcnow() + timedelta(
                hours=self.config.task.duration
            )
            self.save()

            from ..jobs import Job

            job = Job.get(self.job_id)
            if job:
                job.on_start()

    @classmethod
    def key_fields(cls) -> Tuple[str, str]:
        return ("job_id", "task_id")

    @classmethod
    def try_add_heartbeat(cls, raw: Dict) -> bool:
        now = datetime.utcnow()

        try:
            entry = TaskHeartbeatEntry.parse_obj(raw)
            task = cls.try_get_by_task_id(entry.task_id)
            if task:
                try:
                    heartbeats = json.loads(task.heartbeats)
                except ValueError:
                    heartbeats = {}

                for hb in entry.data:
                    for key in hb:
                        hb_type = hb[key]

                        heartbeats[entry.machine_id, hb_type] = TaskHeartbeat(
                            machine_id=entry.machine_id, timestamp=now, type=hb_type
                        )
                task.heartbeats = json.dumps(heartbeats)
                task.save()
            # cls.add(entry)P
            return True
        except ValidationError:
            return False
