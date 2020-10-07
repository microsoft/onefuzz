#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from enum import Enum
from typing import List


class OS(Enum):
    windows = "windows"
    linux = "linux"


class DashboardEvent(Enum):
    heartbeat = "heartbeat"
    new_file = "new_file"
    repro_state = "repro_state"
    task_state = "task_state"
    job_state = "job_state"
    proxy_state = "proxy_state"
    pool_state = "pool_state"
    node_state = "node_state"
    scaleset_state = "scaleset_state"


class TelemetryEvent(Enum):
    task = "task"
    state_changed = "state_changed"

    @classmethod
    def can_share(cls) -> List["TelemetryEvent"]:
        """ only these events will be shared to the central telemetry """
        return [cls.task, cls.state_changed]


class TelemetryData(Enum):
    component_type = "component_type"
    current_state = "current_state"
    job_id = "job_id"
    task_id = "task_id"
    task_type = "task_type"
    vm_id = "vm_id"

    @classmethod
    def can_share(cls) -> List["TelemetryData"]:
        """ only these types of data will be shared to the central telemetry """
        return [cls.current_state, cls.vm_id, cls.job_id, cls.task_id, cls.task_type]


class TaskFeature(Enum):
    input_queue_from_container = "input_queue_from_container"
    supervisor_exe = "supervisor_exe"
    supervisor_env = "supervisor_env"
    supervisor_options = "supervisor_options"
    supervisor_input_marker = "supervisor_input_marker"
    stats_file = "stats_file"
    stats_format = "stats_format"
    target_exe = "target_exe"
    target_env = "target_env"
    target_options = "target_options"
    analyzer_exe = "analyzer_exe"
    analyzer_env = "analyzer_env"
    analyzer_options = "analyzer_options"
    rename_output = "rename_output"
    target_options_merge = "target_options_merge"
    target_workers = "target_workers"
    generator_exe = "generator_exe"
    generator_env = "generator_env"
    generator_options = "generator_options"
    wait_for_files = "wait_for_files"
    target_timeout = "target_timeout"
    check_asan_log = "check_asan_log"
    check_debugger = "check_debugger"
    check_retry_count = "check_retry_count"


# Permissions for an Azure Blob Storage Container.
#
# See: https://docs.microsoft.com/en-us/rest/api/storageservices/create-service-sas#permissions-for-a-container  # noqa: E501
class ContainerPermission(Enum):
    Read = "Read"
    Write = "Write"
    Create = "Create"
    List = "List"
    Delete = "Delete"
    Add = "Add"


class JobState(Enum):
    init = "init"
    enabled = "enabled"
    stopping = "stopping"
    stopped = "stopped"

    @classmethod
    def available(cls) -> List["JobState"]:
        """ set of states that indicate if tasks can be added to it """
        return [x for x in cls if x not in [cls.stopping, cls.stopped]]

    @classmethod
    def needs_work(cls) -> List["JobState"]:
        """
        set of states that indicate work is needed during eventing
        """
        return [cls.init, cls.stopping]


class TaskState(Enum):
    init = "init"
    waiting = "waiting"
    scheduled = "scheduled"
    setting_up = "setting_up"
    running = "running"
    stopping = "stopping"
    stopped = "stopped"
    wait_job = "wait_job"

    @classmethod
    def has_started(cls) -> List["TaskState"]:
        return [cls.running, cls.stopping, cls.stopped]

    @classmethod
    def needs_work(cls) -> List["TaskState"]:
        """
        set of states that indicate work is needed during eventing
        """
        return [cls.init, cls.stopping]

    @classmethod
    def available(cls) -> List["TaskState"]:
        """ set of states that indicate if the task isn't stopping """
        return [x for x in cls if x not in [TaskState.stopping, TaskState.stopped]]

    @classmethod
    def shutting_down(cls) -> List["TaskState"]:
        return [TaskState.stopping, TaskState.stopped]


class TaskType(Enum):
    libfuzzer_fuzz = "libfuzzer_fuzz"
    libfuzzer_coverage = "libfuzzer_coverage"
    libfuzzer_crash_report = "libfuzzer_crash_report"
    libfuzzer_merge = "libfuzzer_merge"
    generic_analysis = "generic_analysis"
    generic_supervisor = "generic_supervisor"
    generic_merge = "generic_merge"
    generic_generator = "generic_generator"
    generic_crash_report = "generic_crash_report"


class VmState(Enum):
    init = "init"
    extensions_launch = "extensions_launch"
    extensions_failed = "extensions_failed"
    vm_allocation_failed = "vm_allocation_failed"
    running = "running"
    stopping = "stopping"
    stopped = "stopped"

    @classmethod
    def needs_work(cls) -> List["VmState"]:
        """
        set of states that indicate work is needed during eventing
        """
        return [cls.init, cls.extensions_launch, cls.stopping]

    @classmethod
    def available(cls) -> List["VmState"]:
        """ set of states that indicate if the repro vm isn't stopping """
        return [x for x in cls if x not in [cls.stopping, cls.stopped]]


class UpdateType(Enum):
    Task = "Task"
    Job = "Job"
    Repro = "Repro"
    Proxy = "Proxy"
    Pool = "Pool"
    Node = "Node"
    Scaleset = "Scaleset"
    TaskScheduler = "TaskScheduler"


class Compare(Enum):
    Equal = "Equal"
    AtLeast = "AtLeast"
    AtMost = "AtMost"


class ContainerType(Enum):
    setup = "setup"
    crashes = "crashes"
    inputs = "inputs"
    readonly_inputs = "readonly_inputs"
    unique_inputs = "unique_inputs"
    coverage = "coverage"
    reports = "reports"
    unique_reports = "unique_reports"
    no_repro = "no_repro"
    tools = "tools"
    analysis = "analysis"


class StatsFormat(Enum):
    AFL = "AFL"


class ErrorCode(Enum):
    INVALID_REQUEST = 450
    INVALID_PERMISSION = 451
    MISSING_EULA_AGREEMENT = 452
    INVALID_JOB = 453
    INVALID_TASK = 453
    UNABLE_TO_ADD_TASK_TO_JOB = 454
    INVALID_CONTAINER = 455
    UNABLE_TO_RESIZE = 456
    UNAUTHORIZED = 457
    UNABLE_TO_USE_STOPPED_JOB = 458
    UNABLE_TO_CHANGE_JOB_DURATION = 459
    UNABLE_TO_CREATE_NETWORK = 460
    VM_CREATE_FAILED = 461
    MISSING_NOTIFICATION = 462
    INVALID_IMAGE = 463
    UNABLE_TO_CREATE = 464
    UNABLE_TO_PORT_FORWARD = 465
    UNABLE_TO_FIND = 467
    TASK_FAILED = 468
    INVALID_NODE = 469
    NOTIFICATION_FAILURE = 470


class HeartbeatType(Enum):
    MachineAlive = "MachineAlive"
    TaskAlive = "TaskAlive"


class PoolType(Enum):
    managed = "managed"
    unmanaged = "unmanaged"


class PoolState(Enum):
    init = "init"
    running = "running"
    shutdown = "shutdown"
    halt = "halt"

    @classmethod
    def needs_work(cls) -> List["PoolState"]:
        """
        set of states that indicate work is needed during eventing
        """
        return [cls.init, cls.shutdown, cls.halt]

    @classmethod
    def available(cls) -> List["PoolState"]:
        """ set of states that indicate if it's available for work """
        return [cls.init, cls.running]


class ScalesetState(Enum):
    init = "init"
    setup = "setup"
    resize = "resize"
    running = "running"
    shutdown = "shutdown"
    halt = "halt"
    creation_failed = "creation_failed"

    @classmethod
    def needs_work(cls) -> List["ScalesetState"]:
        """
        set of states that indicate work is needed during eventing
        """
        return [cls.init, cls.setup, cls.resize, cls.shutdown, cls.halt]

    @classmethod
    def available(cls) -> List["ScalesetState"]:
        """ set of states that indicate if it's available for work """
        unavailable = [cls.shutdown, cls.halt, cls.creation_failed]
        return [x for x in cls if x not in unavailable]


class Architecture(Enum):
    x86_64 = "x86_64"


class NodeTaskState(Enum):
    init = "init"
    setting_up = "setting_up"
    running = "running"


class AgentMode(Enum):
    fuzz = "fuzz"
    repro = "repro"
    proxy = "proxy"


class NodeState(Enum):
    init = "init"
    free = "free"
    setting_up = "setting_up"
    rebooting = "rebooting"
    ready = "ready"
    busy = "busy"
    done = "done"
    shutdown = "shutdown"
    halt = "halt"

    @classmethod
    def needs_work(cls) -> List["NodeState"]:
        return [cls.done, cls.shutdown, cls.halt]

    @classmethod
    def ready_for_reset(cls) -> List["NodeState"]:
        # If Node is in one of these states, ignore updates
        # from the agent.
        return [cls.done, cls.shutdown, cls.halt]


class GithubIssueState(Enum):
    open = "open"
    closed = "closed"


class GithubIssueSearchMatch(Enum):
    title = "title"
    body = "body"
