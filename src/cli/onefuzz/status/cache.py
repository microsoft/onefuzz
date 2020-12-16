#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from datetime import datetime, timedelta, timezone
from enum import Enum
from typing import Any, Dict, List, Optional, Set, Tuple, Union
from uuid import UUID

from onefuzztypes.enums import ContainerType, JobState, NodeState, TaskState, TaskType
from onefuzztypes.events import (
    EventJobCreated,
    EventJobStopped,
    EventMessage,
    EventNodeCreated,
    EventNodeDeleted,
    EventNodeStateUpdated,
    EventPoolCreated,
    EventPoolDeleted,
    EventTaskCreated,
    EventTaskFailed,
    EventTaskStopped,
    EventType,
)
from onefuzztypes.models import Job, JobConfig, Node, Pool, Task, TaskContainers
from pydantic import BaseModel

MESSAGE = Tuple[datetime, EventType, str]

MINUTES = 60
HOURS = 60 * MINUTES
DAYS = 24 * HOURS


class MiniNode(BaseModel):
    machine_id: UUID
    pool_name: str
    state: NodeState


class MiniJob(BaseModel):
    job_id: UUID
    config: JobConfig
    state: Optional[JobState]


class MiniTask(BaseModel):
    job_id: UUID
    task_id: UUID
    type: TaskType
    target: str
    state: Optional[TaskState]
    pool: str
    end_time: Optional[datetime]
    containers: List[TaskContainers]


def fmt_delta(data: timedelta) -> str:
    result = []

    seconds = data.total_seconds()
    for letter, size in [
        ("d", DAYS),
        ("h", HOURS),
        ("m", MINUTES),
    ]:
        part, seconds = divmod(seconds, size)
        if part:
            result.append("%d%s" % (part, letter))

    return "".join(result)


def fmt(data: Any) -> Any:
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
    if isinstance(data, timedelta):
        return fmt_delta(data)
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
    JOB_FIELDS = ["Job", "Name", "Files"]
    TASK_FIELDS = [
        "Job",
        "Task",
        "State",
        "Type",
        "Target",
        "Files",
        "Pool",
        "Time left",
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
        self.files: Dict[str, Tuple[Optional[datetime], Set[str]]] = {}
        self.pools: Dict[str, EventPoolCreated] = {}
        self.nodes: Dict[UUID, MiniNode] = {}

        self.messages: List[MESSAGE] = []
        endpoint = onefuzz._backend.config.endpoint
        if not endpoint:
            raise Exception("endpoint is not set")
        self.endpoint: str = endpoint
        self.last_update = datetime.now()

    def add_container(self, name: str, ignore_date: bool = False) -> None:
        if name in self.files:
            return
        try:
            files = self.onefuzz.containers.files.list(name)
        except Exception:
            return

        self.add_files(name, set(files.files), ignore_date=ignore_date)

    def add_message(self, message: EventMessage) -> None:
        events = {
            EventPoolCreated: lambda x: self.pool_created(x),
            EventPoolDeleted: lambda x: self.pool_deleted(x),
            EventTaskCreated: lambda x: self.task_created(x),
            EventTaskStopped: lambda x: self.task_stopped(x),
            EventTaskFailed: lambda x: self.task_stopped(x),
            EventJobCreated: lambda x: self.job_created(x),
            EventJobStopped: lambda x: self.job_stopped(x),
            EventNodeStateUpdated: lambda x: self.node_state_updated(x),
            EventNodeCreated: lambda x: self.node_created(x),
            EventNodeDeleted: lambda x: self.node_deleted(x),
        }

        for event_cls in events:
            if isinstance(message.event, event_cls):
                events[event_cls](message.event)

        self.last_update = datetime.now()
        messages = [x for x in self.messages][-99:]
        messages += [
            (datetime.now(), message.event_type, message.event.json(exclude_none=True))
        ]
        self.messages = messages

    def add_files(
        self, container: str, new_files: Set[str], ignore_date: bool = False
    ) -> None:
        current_date: Optional[datetime] = None
        if container in self.files:
            (current_date, files) = self.files[container]
        else:
            files = set()
        files.update(new_files)
        if not ignore_date:
            current_date = datetime.now()
        self.files[container] = (current_date, files)

    def add_node(self, node: Node) -> None:
        self.nodes[node.machine_id] = MiniNode(
            machine_id=node.machine_id, state=node.state, pool_name=node.pool_name
        )

    def add_job(self, job: Job) -> MiniJob:
        mini_job = MiniJob(job_id=job.job_id, config=job.config, state=job.state)
        self.jobs[job.job_id] = mini_job
        return mini_job

    def job_created(
        self,
        job: EventJobCreated,
    ) -> None:
        self.jobs[job.job_id] = MiniJob(job_id=job.job_id, config=job.config)

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
            target=task.config.task.target_exe.replace("setup/", "", 0),
            containers=task.config.containers,
        )

    def task_created(self, event: EventTaskCreated) -> None:
        self.tasks[event.task_id] = MiniTask(
            job_id=event.job_id,
            task_id=event.task_id,
            type=event.config.task.type,
            pool=event.config.pool.pool_name if event.config.pool else "",
            target=event.config.task.target_exe.replace("setup/", "", 0),
            containers=event.config.containers,
        )

    def task_stopped(self, event: EventTaskStopped) -> None:
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

            end: Union[str, timedelta] = ""
            if task.end_time:
                end = task.end_time - datetime.now().astimezone(timezone.utc)

            entry = (
                task.job_id,
                task.task_id,
                task.state.name if task.state else "",
                task.type.name,
                task.target,
                files,
                task.pool,
                end,
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
                    self.files[container.name][1]
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
