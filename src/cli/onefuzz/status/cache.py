#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from datetime import datetime, timedelta, timezone
from enum import Enum
from typing import Any, Dict, List, Optional, Set, Tuple, Union
from uuid import UUID

from onefuzztypes.enums import ContainerType, JobState, NodeState, PoolState, TaskState
from onefuzztypes.models import Job, Node, Pool, Task
from pydantic import BaseModel

MESSAGE = Tuple[datetime, str, str]

MINUTES = 60
HOURS = 60 * MINUTES
DAYS = 24 * HOURS


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
    JOB_FIELDS = ["Updated", "State", "Job", "Name", "Files"]
    TASK_FIELDS = [
        "Updated",
        "State",
        "Job",
        "Task",
        "Type",
        "Name",
        "Files",
        "Pool",
        "Time left",
    ]
    POOL_FIELDS = ["Updated", "Pool", "Name", "OS", "State", "Nodes"]

    def __init__(
        self,
        onefuzz: "Onefuzz",
        job_filters: JobFilter,
    ):
        self.onefuzz = onefuzz
        self.job_filters = job_filters
        self.tasks: Dict[UUID, Tuple[datetime, Task]] = {}
        self.jobs: Dict[UUID, Tuple[datetime, Job]] = {}
        self.files: Dict[str, Tuple[Optional[datetime], Set[str]]] = {}
        self.pools: Dict[str, Tuple[datetime, Pool]] = {}
        self.nodes: Dict[UUID, Tuple[datetime, Node]] = {}

        self.messages: List[MESSAGE] = []
        self.endpoint: str = onefuzz._backend.config["endpoint"]
        self.last_update = datetime.now()

    def add_container(self, name: str, ignore_date: bool = False) -> None:
        if name in self.files:
            return
        try:
            files = self.onefuzz.containers.files.list(name)
        except Exception:
            return

        self.add_files(name, set(files.files), ignore_date=ignore_date)

    def add_message(self, name: str, message: Dict[str, Any]) -> None:
        self.last_update = datetime.now()
        data: Dict[str, Union[int, str]] = {}
        for (k, v) in message.items():
            if k in ["task_id", "job_id", "pool_id", "scaleset_id", "machine_id"]:
                k = k.replace("_id", "")
                data[k] = str(v)[:8]
            elif isinstance(v, (int, str)):
                data[k] = v

        as_str = fmt(data)
        messages = [x for x in self.messages if (x[1:] != [name, as_str])][-99:]
        messages += [(datetime.now(), name, as_str)]

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

    def add_node(
        self, machine_id: UUID, state: NodeState, node: Optional[Node] = None
    ) -> None:
        if state in [NodeState.halt]:
            if machine_id in self.nodes:
                del self.nodes[machine_id]
            return

        if machine_id in self.nodes:
            (_, node) = self.nodes[machine_id]
            node.state = state
            self.nodes[machine_id] = (datetime.now(), node)
        else:
            try:
                if not node:
                    node = self.onefuzz.nodes.get(machine_id)
                self.nodes[node.machine_id] = (datetime.now(), node)
            except Exception:
                logging.debug("unable to find pool: %s", machine_id)

    def add_pool(
        self, pool_name: str, state: PoolState, pool: Optional[Pool] = None
    ) -> None:
        if state in [PoolState.halt]:
            if pool_name in self.pools:
                del self.pools[pool_name]
            return

        if pool_name in self.pools:
            (_, pool) = self.pools[pool_name]
            pool.state = state
            self.pools[pool_name] = (datetime.now(), pool)
        else:
            try:
                if not pool:
                    pool = self.onefuzz.pools.get(pool_name)
                self.pools[pool.name] = (datetime.now(), pool)
            except Exception:
                logging.debug("unable to find pool: %s", pool_name)

    def render_pools(self) -> List:
        results = []

        for (timestamp, pool) in sorted(self.pools.values(), key=lambda x: x[0]):
            timestamps = [timestamp]
            nodes = {}
            for (node_ts, node) in self.nodes.values():
                if node.pool_name != pool.name:
                    continue
                if node.state not in nodes:
                    nodes[node.state] = 0
                nodes[node.state] += 1
                timestamps.append(node_ts)

            timestamps = [timestamp]
            entry = [
                max(timestamps),
                pool.pool_id,
                pool.name,
                pool.os,
                pool.state,
                nodes or "None",
            ]
            results.append(entry)
        return results

    def add_task(
        self,
        task_id: UUID,
        state: TaskState,
        add_files: bool = True,
        task: Optional[Task] = None,
    ) -> None:
        if state in [TaskState.stopping, TaskState.stopped]:
            if task_id in self.tasks:
                del self.tasks[task_id]
            return

        if task_id in self.tasks and self.tasks[task_id][1].state != state:
            (_, task) = self.tasks[task_id]
            task.state = state
            self.tasks[task_id] = (datetime.now(), task)
        else:
            try:
                if task is None:
                    task = self.onefuzz.tasks.get(task_id)
                self.add_job_if_missing(task.job_id)
                self.tasks[task.task_id] = (datetime.now(), task)
                if add_files:
                    for container in task.config.containers:
                        self.add_container(container.name)
            except Exception:
                logging.debug("unable to find task: %s", task_id)

    def render_tasks(self) -> List:
        results = []
        for (timestamp, task) in sorted(self.tasks.values(), key=lambda x: x[0]):
            job_entry = self.jobs.get(task.job_id)
            if job_entry:
                (_, job) = job_entry
                if not self.should_render_job(job):
                    continue

            timestamps, files = self.get_file_counts([task])
            timestamps += [timestamp]

            end: Union[str, timedelta] = ""
            if task.end_time:
                end = task.end_time - datetime.now().astimezone(timezone.utc)

            entry = [
                max(timestamps),
                task.state.name,
                task.job_id,
                task.task_id,
                task.config.task.type.name,
                task.config.task.target_exe.replace("setup/", "", 0),
                files,
                task.config.pool.pool_name if task.config.pool else "",
                end,
            ]
            results.append(entry)
        return results

    def add_job_if_missing(self, job_id: UUID) -> None:
        if job_id in self.jobs:
            return
        job = self.onefuzz.jobs.get(job_id)
        self.add_job(job_id, job.state, job)

    def add_job(self, job_id: UUID, state: JobState, job: Optional[Job] = None) -> None:
        if state in [JobState.stopping, JobState.stopped]:
            if job_id in self.jobs:
                del self.jobs[job_id]
            return

        if job_id in self.jobs:
            (_, job) = self.jobs[job_id]
            job.state = state
            self.jobs[job_id] = (datetime.now(), job)
        else:
            try:
                if not job:
                    job = self.onefuzz.jobs.get(job_id)
                self.jobs[job_id] = (datetime.now(), job)
            except Exception:
                logging.debug("unable to find job: %s", job_id)

    def should_render_job(self, job: Job) -> bool:
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

    def render_jobs(self) -> List[Tuple]:
        results: List[Tuple] = []

        for (timestamp, job) in sorted(self.jobs.values(), key=lambda x: x[0]):
            if not self.should_render_job(job):
                continue

            timestamps, files = self.get_file_counts(
                self.get_tasks(job.job_id), merge_inputs=True
            )
            timestamps += [timestamp]

            for to_remove in [ContainerType.coverage, ContainerType.setup]:
                if to_remove in files:
                    del files[to_remove]

            entry = (
                max(timestamps),
                job.state.name,
                job.job_id,
                "%s:%s:%s" % (job.config.project, job.config.name, job.config.build),
                files,
            )
            results.append(entry)

        return results

    def get_file_counts(
        self, tasks: List[Task], merge_inputs: bool = False
    ) -> Tuple[List[datetime], Dict[ContainerType, int]]:
        timestamps = []
        results: Dict[ContainerType, Dict[str, int]] = {}
        for task in tasks:
            for container in task.config.containers:
                if container.name not in self.files:
                    continue
                if merge_inputs and container.type == ContainerType.readonly_inputs:
                    container.type = ContainerType.inputs
                if container.type not in results:
                    results[container.type] = {}
                results[container.type][container.name] = len(
                    self.files[container.name][1]
                )
                container_date = self.files[container.name][0]
                if container_date is not None:
                    timestamps.append(container_date)
        results_merged = {}
        for k, v in results.items():
            value = sum(v.values())
            if value:
                results_merged[k] = value

        return (timestamps, results_merged)

    def get_tasks(self, job_id: UUID) -> List[Task]:
        result = []
        for (_, task) in self.tasks.values():
            if task.job_id == job_id:
                result.append(task)
        return result


from onefuzz.api import Onefuzz  # noqa: E402
