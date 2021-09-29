#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
import logging
from datetime import datetime
from enum import Enum
from typing import Any, Dict, List, Optional, Set, Tuple, Union
from uuid import UUID

from onefuzztypes.enums import ContainerType, JobState, NodeState, TaskState, TaskType
from onefuzztypes.events import (
    EventCrashReported,
    EventFileAdded,
    EventJobCreated,
    EventJobStopped,
    EventNodeCreated,
    EventNodeDeleted,
    EventNodeStateUpdated,
    EventPoolCreated,
    EventPoolDeleted,
    EventTaskCreated,
    EventTaskFailed,
    EventTaskStateUpdated,
    EventTaskStopped,
    parse_event_message,
)
from onefuzztypes.models import (
    Job,
    JobConfig,
    Node,
    Pool,
    Task,
    TaskContainers,
    UserInfo,
)
from onefuzztypes.primitives import Container, PoolName
from pydantic import BaseModel

MESSAGE = Tuple[datetime, str, str]

MINUTES = 60
HOURS = 60 * MINUTES
DAYS = 24 * HOURS


# status-top only representation of a Node
class MiniNode(BaseModel):
    machine_id: UUID
    pool_name: PoolName
    state: NodeState


# status-top only representation of a Job
class MiniJob(BaseModel):
    job_id: UUID
    config: JobConfig
    state: Optional[JobState]
    user_info: Optional[UserInfo]


# status-top only representation of a Task
class MiniTask(BaseModel):
    job_id: UUID
    task_id: UUID
    type: TaskType
    target: str
    state: TaskState
    pool: str
    end_time: Optional[datetime]
    containers: List[TaskContainers]
    vm_count: int


def fmt(data: Any) -> Any:
    if data is None:
        return ""
    if isinstance(data, int):
        return str(data)
    if isinstance(data, str):
        return data
    if isinstance(data, UUID):
        return str(data)[:8]
    if isinstance(data, list):
        return [fmt(x) for x in data]
    if isinstance(data, datetime):
        return data.strftime("%H:%M:%S")
    if isinstance(data, tuple):
        return tuple([fmt(x) for x in data])
    if isinstance(data, Enum):
        return data.name
    if isinstance(data, dict):
        return " ".join(
            sorted(
                [
                    "{}:{}".format(fmt(x).title().replace("_", " "), fmt(y))
                    for (x, y) in data.items()
                ]
            )
        )
    raise NotImplementedError(type(data))


class JobFilter(BaseModel):
    job_id: Optional[List[UUID]]
    project: Optional[List[str]]
    name: Optional[List[str]]


class TopCache:
    JOB_FIELDS = ["Job", "Name", "User", "Files"]
    TASK_FIELDS = [
        "Job",
        "Task",
        "State",
        "Type",
        "Target",
        "Files",
        "Pool",
        "VM Count",
        "End time",
    ]
    POOL_FIELDS = ["Name", "OS", "Arch", "Nodes"]

    def __init__(
        self,
        onefuzz: "Onefuzz",
        job_filters: JobFilter,
    ):
        self.onefuzz = onefuzz
        self.job_filters = job_filters
        self.tasks: Dict[UUID, MiniTask] = {}
        self.jobs: Dict[UUID, MiniJob] = {}
        self.files: Dict[Container, Set[str]] = {}
        self.pools: Dict[str, EventPoolCreated] = {}
        self.nodes: Dict[UUID, MiniNode] = {}

        self.messages: List[MESSAGE] = []
        endpoint = onefuzz._backend.config.endpoint
        if not endpoint:
            raise Exception("endpoint is not set")
        self.endpoint: str = endpoint
        self.last_update = datetime.now()

    def add_container(self, name: Container) -> None:
        if name in self.files:
            return
        try:
            files = self.onefuzz.containers.files.list(name)
        except Exception:
            return

        self.add_files_set(name, set(files.files))

    def add_message(self, message_obj: Any) -> None:
        message = parse_event_message(message_obj)

        event = message.event
        if isinstance(event, EventPoolCreated):
            self.pool_created(event)
        elif isinstance(event, EventPoolDeleted):
            self.pool_deleted(event)
        elif isinstance(event, EventTaskCreated):
            self.task_created(event)
        elif isinstance(event, EventTaskStopped):
            self.task_stopped(event)
        elif isinstance(event, EventTaskFailed):
            self.task_failed(event)
        elif isinstance(event, EventTaskStateUpdated):
            self.task_state_updated(event)
        elif isinstance(event, EventJobCreated):
            self.job_created(event)
        elif isinstance(event, EventJobStopped):
            self.job_stopped(event)
        elif isinstance(event, EventNodeStateUpdated):
            self.node_state_updated(event)
        elif isinstance(event, EventNodeCreated):
            self.node_created(event)
        elif isinstance(event, EventNodeDeleted):
            self.node_deleted(event)
        elif isinstance(event, (EventCrashReported, EventFileAdded)):
            self.file_added(event)

        self.last_update = datetime.now()
        messages = [x for x in self.messages][-99:]
        messages += [
            (
                datetime.now(),
                message.event_type.name,
                json.dumps(message_obj, sort_keys=True),
            )
        ]
        self.messages = messages

    def file_added(self, event: Union[EventFileAdded, EventCrashReported]) -> None:
        if event.container in self.files:
            files = self.files[event.container]
        else:
            files = set()
        files.update(set([event.filename]))
        self.files[event.container] = files

    def add_files_set(self, container: Container, new_files: Set[str]) -> None:
        if container in self.files:
            files = self.files[container]
        else:
            files = set()
        files.update(new_files)
        self.files[container] = files

    def add_node(self, node: Node) -> None:
        self.nodes[node.machine_id] = MiniNode(
            machine_id=node.machine_id, state=node.state, pool_name=node.pool_name
        )

    def add_job(self, job: Job) -> MiniJob:
        mini_job = MiniJob(
            job_id=job.job_id,
            config=job.config,
            state=job.state,
            user_info=job.user_info,
        )
        self.jobs[job.job_id] = mini_job
        return mini_job

    def job_created(
        self,
        job: EventJobCreated,
    ) -> None:
        self.jobs[job.job_id] = MiniJob(
            job_id=job.job_id, config=job.config, user_info=job.user_info
        )

    def add_pool(self, pool: Pool) -> None:
        self.pool_created(
            EventPoolCreated(
                pool_name=pool.name,
                os=pool.os,
                arch=pool.arch,
                managed=pool.managed,
            )
        )

    def pool_created(
        self,
        pool: EventPoolCreated,
    ) -> None:
        self.pools[pool.pool_name] = pool

    def pool_deleted(self, pool: EventPoolDeleted) -> None:
        if pool.pool_name in self.pools:
            del self.pools[pool.pool_name]

    def render_pools(self) -> List:
        results = []
        for pool in self.pools.values():
            nodes = {}
            for node in self.nodes.values():
                if node.pool_name != pool.pool_name:
                    continue
                if node.state not in nodes:
                    nodes[node.state] = 0
                nodes[node.state] += 1
            entry = (pool.pool_name, pool.os, pool.arch, nodes or "None")
            results.append(entry)
        return results

    def node_created(self, node: EventNodeCreated) -> None:
        self.nodes[node.machine_id] = MiniNode(
            machine_id=node.machine_id, pool_name=node.pool_name, state=NodeState.init
        )

    def node_state_updated(self, node: EventNodeStateUpdated) -> None:
        self.nodes[node.machine_id] = MiniNode(
            machine_id=node.machine_id, pool_name=node.pool_name, state=node.state
        )

    def node_deleted(self, node: EventNodeDeleted) -> None:
        if node.machine_id in self.nodes:
            del self.nodes[node.machine_id]

    def add_task(self, task: Task) -> None:
        self.tasks[task.task_id] = MiniTask(
            job_id=task.job_id,
            task_id=task.task_id,
            type=task.config.task.type,
            pool=task.config.pool.pool_name if task.config.pool else "",
            state=task.state,
            target=(task.config.task.target_exe or "").replace("setup/", "", 0),
            containers=task.config.containers,
            end_time=task.end_time,
            vm_count=task.config.pool.count if task.config.pool else 0,
        )

    def task_created(self, event: EventTaskCreated) -> None:
        self.tasks[event.task_id] = MiniTask(
            job_id=event.job_id,
            task_id=event.task_id,
            type=event.config.task.type,
            pool=event.config.pool.pool_name if event.config.pool else "",
            target=(event.config.task.target_exe or "").replace("setup/", "", 0),
            containers=event.config.containers,
            state=TaskState.init,
            vm_count=event.config.pool.count if event.config.pool else 0,
        )

    def task_state_updated(self, event: EventTaskStateUpdated) -> None:
        if event.task_id in self.tasks:
            task = self.tasks[event.task_id]
            task.state = event.state
            task.end_time = event.end_time
            self.tasks[event.task_id] = task

    def task_stopped(self, event: EventTaskStopped) -> None:
        if event.task_id in self.tasks:
            del self.tasks[event.task_id]

    def task_failed(self, event: EventTaskFailed) -> None:
        if event.task_id in self.tasks:
            del self.tasks[event.task_id]

    def render_tasks(self) -> List:
        results = []
        for task in self.tasks.values():
            job_entry = self.jobs.get(task.job_id)
            if job_entry:
                if not self.should_render_job(job_entry):
                    continue

            files = self.get_file_counts([task])

            entry = (
                task.job_id,
                task.task_id,
                task.state,
                task.type.name,
                task.target,
                files,
                task.pool,
                task.vm_count,
                task.end_time,
            )
            results.append(entry)
        return results

    def add_job_if_missing(self, job_id: UUID) -> None:
        if job_id in self.jobs:
            return
        job = self.onefuzz.jobs.get(job_id)
        self.add_job(job)

    def should_render_job(self, job: MiniJob) -> bool:
        if self.job_filters.job_id is not None:
            if job.job_id not in self.job_filters.job_id:
                logging.info("skipping:%s", job)
                return False

        if self.job_filters.project is not None:
            if job.config.project not in self.job_filters.project:
                logging.info("skipping:%s", job)
                return False

        if self.job_filters.name is not None:
            if job.config.name not in self.job_filters.name:
                logging.info("skipping:%s", job)
                return False

        return True

    def job_stopped(self, event: EventJobStopped) -> None:
        if event.job_id in self.jobs:
            del self.jobs[event.job_id]

        to_remove = [x.task_id for x in self.tasks.values() if x.job_id == event.job_id]

        for task_id in to_remove:
            del self.tasks[task_id]

    def render_jobs(self) -> List[Tuple]:
        results: List[Tuple] = []

        for job in self.jobs.values():
            if not self.should_render_job(job):
                continue

            files = self.get_file_counts(self.get_tasks(job.job_id), merge_inputs=True)

            for to_remove in [ContainerType.coverage, ContainerType.setup]:
                if to_remove in files:
                    del files[to_remove]

            entry = (
                job.job_id,
                "%s:%s:%s" % (job.config.project, job.config.name, job.config.build),
                job.user_info.upn if job.user_info else "",
                files,
            )
            results.append(entry)

        return results

    def get_file_counts(
        self, tasks: List[MiniTask], merge_inputs: bool = False
    ) -> Dict[ContainerType, int]:
        results: Dict[ContainerType, Dict[str, int]] = {}
        for task in tasks:
            for container in task.containers:
                if container.name not in self.files:
                    continue
                if merge_inputs and container.type == ContainerType.readonly_inputs:
                    container.type = ContainerType.inputs
                if container.type not in results:
                    results[container.type] = {}
                results[container.type][container.name] = len(
                    self.files[container.name]
                )

        results_merged = {}
        for k, v in results.items():
            value = sum(v.values())
            if value:
                results_merged[k] = value

        return results_merged

    def get_tasks(self, job_id: UUID) -> List[MiniTask]:
        result = []
        for task in self.tasks.values():
            if task.job_id == job_id:
                result.append(task)
        return result


from onefuzz.api import Onefuzz  # noqa: E402
