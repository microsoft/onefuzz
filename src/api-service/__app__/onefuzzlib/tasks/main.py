#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from datetime import datetime, timedelta
from typing import List, Optional, Tuple, Union
from uuid import UUID

from onefuzztypes.enums import ErrorCode, TaskState
from onefuzztypes.events import (
    EventTaskCreated,
    EventTaskFailed,
    EventTaskStateUpdated,
    EventTaskStopped,
)
from onefuzztypes.models import Error
from onefuzztypes.models import Task as BASE_TASK
from onefuzztypes.models import TaskConfig, TaskVm, UserInfo

from ..azure.image import get_os
from ..azure.queue import create_queue, delete_queue
from ..azure.storage import StorageType
from ..events import send_event
from ..orm import MappingIntStrAny, ORMMixin, QueryFilter
from ..workers.nodes import Node, NodeTasks
from ..workers.pools import Pool
from ..workers.scalesets import Scaleset


class Task(BASE_TASK, ORMMixin):
    def check_prereq_tasks(self) -> bool:
        if self.config.prereq_tasks:
            for task_id in self.config.prereq_tasks:
                task = Task.get_by_task_id(task_id)
                # if a prereq task fails, then mark this task as failed
                if isinstance(task, Error):
                    self.mark_failed(task)
                    return False

                if task.state not in task.state.has_started():
                    return False
        return True

    @classmethod
    def create(
        cls, config: TaskConfig, job_id: UUID, user_info: UserInfo
    ) -> Union["Task", Error]:
        if config.vm:
            os = get_os(config.vm.region, config.vm.image)
            if isinstance(os, Error):
                return os
        elif config.pool:
            pool = Pool.get_by_name(config.pool.pool_name)
            if isinstance(pool, Error):
                return pool
            os = pool.os
        else:
            raise Exception("task must have vm or pool")
        task = cls(config=config, job_id=job_id, os=os, user_info=user_info)
        task.save()
        send_event(
            EventTaskCreated(
                job_id=task.job_id,
                task_id=task.task_id,
                config=config,
                user_info=user_info,
            )
        )

        logging.info(
            "created task. job_id:%s task_id:%s type:%s",
            task.job_id,
            task.task_id,
            task.config.task.type.name,
        )
        return task

    def save_exclude(self) -> Optional[MappingIntStrAny]:
        return {"heartbeats": ...}

    def is_ready(self) -> bool:
        if self.config.prereq_tasks:
            for prereq_id in self.config.prereq_tasks:
                prereq = Task.get_by_task_id(prereq_id)
                if isinstance(prereq, Error):
                    logging.info("task prereq has error: %s - %s", self.task_id, prereq)
                    self.mark_failed(prereq)
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

    def init(self) -> None:
        create_queue(self.task_id, StorageType.corpus)
        self.set_state(TaskState.waiting)

    def stopping(self) -> None:
        logging.info("stopping task: %s:%s", self.job_id, self.task_id)
        Node.stop_task(self.task_id)
        if not NodeTasks.get_nodes_by_task_id(self.task_id):
            self.stopped()

    def stopped(self) -> None:
        self.set_state(TaskState.stopped)
        delete_queue(str(self.task_id), StorageType.corpus)

        # TODO: we need to 'unschedule' this task from the existing pools
        from ..jobs import Job

        job = Job.get(self.job_id)
        if job:
            job.stop_if_all_done()

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

    def mark_stopping(self) -> None:
        if self.state in TaskState.shutting_down():
            logging.debug(
                "ignoring post-task stop calls to stop %s:%s", self.job_id, self.task_id
            )
            return

        if self.state not in TaskState.has_started():
            self.mark_failed(
                Error(code=ErrorCode.TASK_FAILED, errors=["task never started"])
            )

        self.set_state(TaskState.stopping)

    def mark_failed(
        self, error: Error, tasks_in_job: Optional[List["Task"]] = None
    ) -> None:
        if self.state in TaskState.shutting_down():
            logging.debug(
                "ignoring post-task stop failures for %s:%s", self.job_id, self.task_id
            )
            return

        if self.error is not None:
            logging.debug(
                "ignoring additional task error %s:%s", self.job_id, self.task_id
            )
            return

        logging.error("task failed %s:%s - %s", self.job_id, self.task_id, error)

        self.error = error
        self.set_state(TaskState.stopping)

        self.mark_dependants_failed(tasks_in_job=tasks_in_job)

    def mark_dependants_failed(
        self, tasks_in_job: Optional[List["Task"]] = None
    ) -> None:
        if tasks_in_job is None:
            tasks_in_job = Task.search(query={"job_id": [self.job_id]})

        for task in tasks_in_job:
            if task.config.prereq_tasks:
                if self.task_id in task.config.prereq_tasks:
                    task.mark_failed(
                        Error(
                            code=ErrorCode.TASK_FAILED,
                            errors=[
                                "prerequisite task failed.  task_id:%s" % self.task_id
                            ],
                        ),
                        tasks_in_job,
                    )

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

            from ..jobs import Job

            job = Job.get(self.job_id)
            if job:
                job.on_start()

    @classmethod
    def key_fields(cls) -> Tuple[str, str]:
        return ("job_id", "task_id")

    def set_state(self, state: TaskState) -> None:
        if self.state == state:
            return

        self.state = state
        if self.state in [TaskState.running, TaskState.setting_up]:
            self.on_start()

        self.save()

        if self.state == TaskState.stopped:
            if self.error:
                send_event(
                    EventTaskFailed(
                        job_id=self.job_id,
                        task_id=self.task_id,
                        error=self.error,
                        user_info=self.user_info,
                        config=self.config,
                    )
                )
            else:
                send_event(
                    EventTaskStopped(
                        job_id=self.job_id,
                        task_id=self.task_id,
                        user_info=self.user_info,
                        config=self.config,
                    )
                )
        else:
            send_event(
                EventTaskStateUpdated(
                    job_id=self.job_id,
                    task_id=self.task_id,
                    state=self.state,
                    end_time=self.end_time,
                    config=self.config,
                )
            )
