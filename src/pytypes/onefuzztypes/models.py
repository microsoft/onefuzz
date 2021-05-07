#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from datetime import datetime
from typing import Any, Dict, Generic, List, Optional, Tuple, TypeVar, Union
from uuid import UUID, uuid4

from pydantic import BaseModel, Field, root_validator, validator
from pydantic.dataclasses import dataclass

from .consts import ONE_HOUR, SEVEN_DAYS
from .enums import (
    OS,
    Architecture,
    Compare,
    ContainerPermission,
    ContainerType,
    ErrorCode,
    GithubIssueSearchMatch,
    GithubIssueState,
    HeartbeatType,
    JobState,
    NodeState,
    NodeTaskState,
    PoolState,
    ScalesetState,
    StatsFormat,
    TaskDebugFlag,
    TaskFeature,
    TaskState,
    TaskType,
    VmState,
)
from .primitives import Container, PoolName, Region


class UserInfo(BaseModel):
    application_id: Optional[UUID]
    object_id: Optional[UUID]
    upn: Optional[str]


# Stores the address of a secret
class SecretAddress(BaseModel):
    # keyvault address of a secret
    url: str


T = TypeVar("T")


# This class allows us to store some data that are intended to be secret
# The secret field stores either the raw data or the address of that data
# This class allows us to maintain backward compatibility with existing
# NotificationTemplate classes
@dataclass
class SecretData(Generic[T]):
    secret: Union[T, SecretAddress]

    def __init__(self, secret: Union[T, SecretAddress]):
        if isinstance(secret, dict):
            self.secret = SecretAddress.parse_obj(secret)
        else:
            self.secret = secret

    def __str__(self) -> str:
        return self.__repr__()

    def __repr__(self) -> str:
        if isinstance(self.secret, SecretAddress):
            return str(self.secret)
        else:
            return "[REDACTED]"


class EnumModel(BaseModel):
    @root_validator(pre=True)
    def exactly_one(cls: Any, values: Any) -> Any:
        some = []

        for field, val in values.items():
            if val is not None:
                some.append(field)

        if not some:
            raise ValueError("no variant set for enum")

        if len(some) > 1:
            raise ValueError("multiple values set for enum: %s" % some)

        return values


class Error(BaseModel):
    code: ErrorCode
    errors: List[str]


OkType = TypeVar("OkType")
Result = Union[OkType, Error]


class FileEntry(BaseModel):
    container: Container
    filename: str
    sas_url: Optional[str]


class Authentication(BaseModel):
    password: str
    public_key: str
    private_key: str


class JobConfig(BaseModel):
    project: str
    name: str
    build: str
    duration: int

    @validator("duration", allow_reuse=True)
    def check_duration(cls, value: int) -> int:
        if value < ONE_HOUR or value > SEVEN_DAYS:
            raise ValueError("invalid duration")
        return value


class ReproConfig(BaseModel):
    container: Container
    path: str
    duration: int

    @validator("duration", allow_reuse=True)
    def check_duration(cls, value: int) -> int:
        if value < ONE_HOUR or value > SEVEN_DAYS:
            raise ValueError("invalid duration")
        return value


class TaskDetails(BaseModel):
    type: TaskType
    duration: int
    target_exe: Optional[str]
    target_env: Optional[Dict[str, str]]
    target_options: Optional[List[str]]
    target_workers: Optional[int]
    target_options_merge: Optional[bool]
    check_asan_log: Optional[bool]
    check_debugger: Optional[bool] = Field(default=True)
    check_retry_count: Optional[int]
    check_fuzzer_help: Optional[bool]
    expect_crash_on_failure: Optional[bool]
    rename_output: Optional[bool]
    supervisor_exe: Optional[str]
    supervisor_env: Optional[Dict[str, str]]
    supervisor_options: Optional[List[str]]
    supervisor_input_marker: Optional[str]
    generator_exe: Optional[str]
    generator_env: Optional[Dict[str, str]]
    generator_options: Optional[List[str]]
    analyzer_exe: Optional[str]
    analyzer_env: Optional[Dict[str, str]]
    analyzer_options: Optional[List[str]]
    wait_for_files: Optional[ContainerType]
    stats_file: Optional[str]
    stats_format: Optional[StatsFormat]
    reboot_after_setup: Optional[bool]
    target_timeout: Optional[int]
    ensemble_sync_delay: Optional[int]
    preserve_existing_outputs: Optional[bool]
    report_list: Optional[List[str]]
    minimized_stack_depth: Optional[int]

    @validator("check_retry_count", allow_reuse=True)
    def validate_check_retry_count(cls, value: int) -> int:
        if value is not None:
            if value < 0:
                raise ValueError("invalid check_retry_count")
        return value

    @validator("target_timeout", allow_reuse=True)
    def check_target_timeout(cls, value: Optional[int]) -> Optional[int]:
        if value is not None:
            if value < 1:
                raise ValueError("invalid target_timeout")
        return value

    @validator("duration", allow_reuse=True)
    def check_duration(cls, value: int) -> int:
        if value < ONE_HOUR or value > SEVEN_DAYS:
            raise ValueError("invalid duration")
        return value


class TaskPool(BaseModel):
    count: int
    pool_name: PoolName


class TaskVm(BaseModel):
    region: Region
    sku: str
    image: str
    count: int = Field(default=1)
    spot_instances: bool = Field(default=False)
    reboot_after_setup: Optional[bool]

    @validator("count", allow_reuse=True)
    def check_count(cls, value: int) -> int:
        if value <= 0:
            raise ValueError("invalid count")
        return value


class TaskContainers(BaseModel):
    type: ContainerType
    name: Container


class TaskConfig(BaseModel):
    job_id: UUID
    prereq_tasks: Optional[List[UUID]]
    task: TaskDetails
    vm: Optional[TaskVm]
    pool: Optional[TaskPool]
    containers: List[TaskContainers]
    tags: Dict[str, str]
    debug: Optional[List[TaskDebugFlag]]
    colocate: Optional[bool]


class BlobRef(BaseModel):
    account: str
    container: Container
    name: str


class Report(BaseModel):
    input_url: Optional[str]
    input_blob: Optional[BlobRef]
    executable: str
    crash_type: str
    crash_site: str
    call_stack: List[str]
    call_stack_sha256: str
    input_sha256: str
    asan_log: Optional[str]
    task_id: UUID
    job_id: UUID
    scariness_score: Optional[int]
    scariness_description: Optional[str]
    minimized_stack: Optional[List[str]]
    minimized_stack_sha256: Optional[str]
    minimized_stack_function_names: Optional[List[str]]
    minimized_stack_function_names_sha256: Optional[str]


class NoReproReport(BaseModel):
    input_sha256: str
    input_blob: Optional[BlobRef]
    executable: str
    task_id: UUID
    job_id: UUID
    tries: int
    error: Optional[str]


class CrashTestResult(BaseModel):
    crash_report: Optional[Report]
    no_repro: Optional[NoReproReport]


class RegressionReport(BaseModel):
    crash_test_result: CrashTestResult
    original_crash_test_result: Optional[CrashTestResult]


class ADODuplicateTemplate(BaseModel):
    increment: List[str]
    comment: Optional[str]
    set_state: Dict[str, str]
    ado_fields: Dict[str, str]


class ADOTemplate(BaseModel):
    base_url: str
    auth_token: SecretData[str]
    project: str
    type: str
    unique_fields: List[str]
    comment: Optional[str]
    ado_fields: Dict[str, str]
    on_duplicate: ADODuplicateTemplate

    # validator needed for backward compatibility
    @validator("auth_token", pre=True, always=True)
    def validate_auth_token(cls, v: Any) -> SecretData:
        if isinstance(v, str):
            return SecretData(secret=v)
        elif isinstance(v, SecretData):
            return v
        elif isinstance(v, dict):
            return SecretData(secret=v["secret"])
        else:
            raise TypeError(f"invalid datatype {type(v)}")


class TeamsTemplate(BaseModel):
    url: SecretData[str]

    # validator needed for backward compatibility
    @validator("url", pre=True, always=True)
    def validate_url(cls, v: Any) -> SecretData:
        if isinstance(v, str):
            return SecretData(secret=v)
        elif isinstance(v, SecretData):
            return v
        elif isinstance(v, dict):
            return SecretData(secret=v["secret"])
        else:
            raise TypeError(f"invalid datatype {type(v)}")


class ContainerDefinition(BaseModel):
    type: ContainerType
    compare: Compare
    value: int
    permissions: List[ContainerPermission]


class VmDefinition(BaseModel):
    compare: Compare
    value: int


class TaskDefinition(BaseModel):
    features: List[TaskFeature]
    containers: List[ContainerDefinition]
    monitor_queue: Optional[ContainerType]
    vm: VmDefinition


# TODO: service shouldn't pass SyncedDir, but just the url and let the agent
# come up with paths
class SyncedDir(BaseModel):
    path: str
    url: str


CONTAINER_DEF = Optional[Union[SyncedDir, List[SyncedDir]]]


class ClientCredentials(BaseModel):
    client_id: UUID
    client_secret: str


class AgentConfig(BaseModel):
    client_credentials: Optional[ClientCredentials]
    onefuzz_url: str
    pool_name: PoolName
    heartbeat_queue: Optional[str]
    instance_telemetry_key: Optional[str]
    microsoft_telemetry_key: Optional[str]
    multi_tenant_domain: Optional[str]
    instance_id: UUID


class TaskUnitConfig(BaseModel):
    instance_id: UUID
    job_id: UUID
    task_id: UUID
    task_type: TaskType
    instance_telemetry_key: Optional[str]
    microsoft_telemetry_key: Optional[str]
    heartbeat_queue: str
    # command_queue: str
    input_queue: Optional[str]
    supervisor_exe: Optional[str]
    supervisor_env: Optional[Dict[str, str]]
    supervisor_options: Optional[List[str]]
    supervisor_input_marker: Optional[str]
    target_exe: Optional[str]
    target_env: Optional[Dict[str, str]]
    target_options: Optional[List[str]]
    target_timeout: Optional[int]
    target_options_merge: Optional[bool]
    target_workers: Optional[int]
    check_asan_log: Optional[bool]
    check_debugger: Optional[bool]
    check_retry_count: Optional[int]
    check_fuzzer_help: Optional[bool]
    expect_crash_on_failure: Optional[bool]
    rename_output: Optional[bool]
    generator_exe: Optional[str]
    generator_env: Optional[Dict[str, str]]
    generator_options: Optional[List[str]]
    wait_for_files: Optional[str]
    analyzer_exe: Optional[str]
    analyzer_env: Optional[Dict[str, str]]
    analyzer_options: Optional[List[str]]
    stats_file: Optional[str]
    stats_format: Optional[StatsFormat]
    ensemble_sync_delay: Optional[int]
    report_list: Optional[List[str]]
    minimized_stack_depth: Optional[int]

    # from here forwards are Container definitions.  These need to be inline
    # with TaskDefinitions and ContainerTypes
    analysis: CONTAINER_DEF
    coverage: CONTAINER_DEF
    crashes: CONTAINER_DEF
    inputs: CONTAINER_DEF
    no_repro: CONTAINER_DEF
    readonly_inputs: CONTAINER_DEF
    reports: CONTAINER_DEF
    tools: CONTAINER_DEF
    unique_inputs: CONTAINER_DEF
    unique_reports: CONTAINER_DEF
    regression_reports: CONTAINER_DEF


class Forward(BaseModel):
    src_port: int
    dst_ip: str
    dst_port: int


class ProxyConfig(BaseModel):
    url: str
    notification: str
    region: Region
    forwards: List[Forward]
    instance_telemetry_key: Optional[str]
    microsoft_telemetry_key: Optional[str]
    instance_id: UUID


class ProxyHeartbeat(BaseModel):
    region: Region
    forwards: List[Forward]
    timestamp: datetime = Field(default_factory=datetime.utcnow)


class Files(BaseModel):
    files: List[str]


class WorkUnit(BaseModel):
    job_id: UUID
    task_id: UUID
    task_type: TaskType

    # JSON-serialized `TaskUnitConfig`.
    config: str


class WorkSet(BaseModel):
    reboot: bool
    setup_url: str
    script: bool
    work_units: List[WorkUnit]


class WorkUnitSummary(BaseModel):
    job_id: UUID
    task_id: UUID
    task_type: TaskType


class WorkSetSummary(BaseModel):
    work_units: List[WorkUnitSummary]


class GithubIssueDuplicate(BaseModel):
    comment: Optional[str]
    labels: List[str]
    reopen: bool


class GithubIssueSearch(BaseModel):
    author: Optional[str]
    state: Optional[GithubIssueState]
    field_match: List[GithubIssueSearchMatch]
    string: str


class GithubAuth(BaseModel):
    user: str
    personal_access_token: str


class GithubIssueTemplate(BaseModel):
    auth: SecretData[GithubAuth]
    organization: str
    repository: str
    title: str
    body: str
    unique_search: GithubIssueSearch
    assignees: List[str]
    labels: List[str]
    on_duplicate: GithubIssueDuplicate

    # validator needed for backward compatibility
    @validator("auth", pre=True, always=True)
    def validate_auth(cls, v: Any) -> SecretData:
        if isinstance(v, str):
            return SecretData(secret=v)
        elif isinstance(v, SecretData):
            return v
        elif isinstance(v, dict):
            try:
                return SecretData(GithubAuth.parse_obj(v))
            except Exception:
                return SecretData(secret=v["secret"])
        else:
            raise TypeError(f"invalid datatype {type(v)}")


NotificationTemplate = Union[ADOTemplate, TeamsTemplate, GithubIssueTemplate]


class Notification(BaseModel):
    container: Container
    notification_id: UUID = Field(default_factory=uuid4)
    config: NotificationTemplate


class JobTaskInfo(BaseModel):
    task_id: UUID
    type: TaskType
    state: TaskState


class Job(BaseModel):
    timestamp: Optional[datetime] = Field(alias="Timestamp")
    job_id: UUID = Field(default_factory=uuid4)
    state: JobState = Field(default=JobState.init)
    config: JobConfig
    error: Optional[str]
    end_time: Optional[datetime] = None
    task_info: Optional[List[JobTaskInfo]]
    user_info: Optional[UserInfo]


class TaskHeartbeatEntry(BaseModel):
    task_id: UUID
    job_id: Optional[UUID]
    machine_id: UUID
    data: List[Dict[str, HeartbeatType]]


class NodeHeartbeatEntry(BaseModel):
    node_id: UUID
    data: List[Dict[str, HeartbeatType]]


class NodeCommandStopIfFree(BaseModel):
    pass


class StopNodeCommand(BaseModel):
    pass


class StopTaskNodeCommand(BaseModel):
    task_id: UUID


class NodeCommandAddSshKey(BaseModel):
    public_key: str


class NodeCommand(EnumModel):
    stop: Optional[StopNodeCommand]
    stop_task: Optional[StopTaskNodeCommand]
    add_ssh_key: Optional[NodeCommandAddSshKey]
    stop_if_free: Optional[NodeCommandStopIfFree]


class NodeCommandEnvelope(BaseModel):
    command: NodeCommand
    message_id: str


class Node(BaseModel):
    timestamp: Optional[datetime] = Field(alias="Timestamp")
    pool_name: PoolName
    machine_id: UUID
    state: NodeState = Field(default=NodeState.init)
    scaleset_id: Optional[UUID] = None
    tasks: Optional[List[Tuple[UUID, NodeTaskState]]] = None
    messages: Optional[List[NodeCommand]] = None
    heartbeat: Optional[datetime]
    version: str = Field(default="1.0.0")
    reimage_requested: bool = Field(default=False)
    delete_requested: bool = Field(default=False)
    debug_keep_node: bool = Field(default=False)


class ScalesetSummary(BaseModel):
    scaleset_id: UUID
    state: ScalesetState


class NodeTasks(BaseModel):
    machine_id: UUID
    task_id: UUID
    state: NodeTaskState = Field(default=NodeTaskState.init)


class AutoScaleConfig(BaseModel):
    image: str
    max_size: Optional[int]  # max size of pool
    min_size: int = Field(default=0)  # min size of pool
    region: Optional[Region]
    scaleset_size: int  # Individual scaleset size
    spot_instances: bool = Field(default=False)
    ephemeral_os_disks: bool = Field(default=False)
    vm_sku: str

    @validator("scaleset_size", allow_reuse=True)
    def check_scaleset_size(cls, value: int) -> int:
        if value < 1 or value > 1000:
            raise ValueError("invalid scaleset size")
        return value

    @root_validator()
    def check_data(cls, values: Any) -> Any:
        if (
            "max_size" in values
            and values.get("max_size")
            and values.get("min_size") > values.get("max_size")
        ):
            raise ValueError("The pool min_size is greater than max_size")
        return values

    @validator("max_size", allow_reuse=True)
    def check_max_size(cls, value: Optional[int]) -> Optional[int]:
        if value and value < 1:
            raise ValueError("Autoscale sizes are not defined properly")
        return value

    @validator("min_size", allow_reuse=True)
    def check_min_size(cls, value: int) -> int:
        if value < 0 or value > 1000:
            raise ValueError("Invalid pool min_size")
        return value


class Pool(BaseModel):
    timestamp: Optional[datetime] = Field(alias="Timestamp")
    name: PoolName
    pool_id: UUID = Field(default_factory=uuid4)
    os: OS
    managed: bool
    autoscale: Optional[AutoScaleConfig]
    arch: Architecture
    state: PoolState = Field(default=PoolState.init)
    client_id: Optional[UUID]
    nodes: Optional[List[Node]]
    config: Optional[AgentConfig]

    # work_queue is explicitly not saved to Tables (see save_exclude).  This is
    # intended to be used to pass the information to the CLI when the CLI asks
    # for information about what work is in the queue for the pool.
    work_queue: Optional[List[WorkSetSummary]]

    # explicitly excluded from Tables
    scaleset_summary: Optional[List[ScalesetSummary]]


class ScalesetNodeState(BaseModel):
    machine_id: UUID
    instance_id: str
    state: Optional[NodeState]


class Scaleset(BaseModel):
    timestamp: Optional[datetime] = Field(alias="Timestamp")
    pool_name: PoolName
    scaleset_id: UUID = Field(default_factory=uuid4)
    state: ScalesetState = Field(default=ScalesetState.init)
    auth: Optional[Authentication]
    vm_sku: str
    image: str
    region: Region
    size: int
    spot_instances: bool
    ephemeral_os_disks: bool = Field(default=False)
    needs_config_update: bool = Field(default=False)
    error: Optional[Error]
    nodes: Optional[List[ScalesetNodeState]]
    client_id: Optional[UUID]
    client_object_id: Optional[UUID]
    tags: Dict[str, str] = Field(default_factory=lambda: {})

    @validator("size", allow_reuse=True)
    def check_size(cls, value: int) -> int:
        if value < 0:
            raise ValueError("Invalid scaleset size")
        return value


class NotificationConfig(BaseModel):
    config: NotificationTemplate


class Repro(BaseModel):
    timestamp: Optional[datetime] = Field(alias="Timestamp")
    vm_id: UUID = Field(default_factory=uuid4)
    task_id: UUID
    config: ReproConfig
    state: VmState = Field(default=VmState.init)
    auth: Optional[Authentication]
    os: OS
    error: Optional[Error]
    ip: Optional[str]
    end_time: Optional[datetime]
    user_info: Optional[UserInfo]


class ExitStatus(BaseModel):
    code: Optional[int]
    signal: Optional[int]
    success: bool


class ProcessOutput(BaseModel):
    exit_status: ExitStatus
    stderr: str
    stdout: str


class WorkerRunningEvent(BaseModel):
    task_id: UUID


class WorkerDoneEvent(BaseModel):
    task_id: UUID
    exit_status: ExitStatus
    stderr: str
    stdout: str


class WorkerEvent(EnumModel):
    done: Optional[WorkerDoneEvent]
    running: Optional[WorkerRunningEvent]


class NodeSettingUpEventData(BaseModel):
    tasks: List[UUID]


class NodeDoneEventData(BaseModel):
    error: Optional[str]
    script_output: Optional[ProcessOutput]


NodeStateData = Union[NodeSettingUpEventData, NodeDoneEventData]


class NodeStateUpdate(BaseModel):
    state: NodeState
    data: Optional[NodeStateData]

    @root_validator(pre=False, skip_on_failure=True)
    def check_data(cls, values: Any) -> Any:
        data = values.get("data")

        if data:
            state = values["state"]

            if state == NodeState.setting_up:
                if isinstance(data, NodeSettingUpEventData):
                    return values

            if state == NodeState.done:
                if isinstance(data, NodeDoneEventData):
                    return values

            raise ValueError(
                "data for node state update event does not match state = %s" % state
            )
        else:
            # For now, `data` is always optional.
            return values


class NodeEvent(EnumModel):
    state_update: Optional[NodeStateUpdate]
    worker_event: Optional[WorkerEvent]


# Temporary shim type to support hot upgrade of 1.0.0 nodes.
#
# We want future variants to use an externally-tagged repr.
NodeEventShim = Union[NodeStateUpdate, NodeEvent, WorkerEvent]


class NodeEventEnvelope(BaseModel):
    machine_id: UUID
    event: NodeEventShim


class TaskEvent(BaseModel):
    timestamp: Optional[datetime] = Field(alias="Timestamp")
    task_id: UUID
    machine_id: UUID
    event_data: WorkerEvent


class TaskEventSummary(BaseModel):
    timestamp: Optional[datetime]
    event_data: str
    event_type: str


class NodeAssignment(BaseModel):
    node_id: UUID
    scaleset_id: Optional[UUID]
    state: NodeTaskState


class Task(BaseModel):
    timestamp: Optional[datetime] = Field(alias="Timestamp")
    job_id: UUID
    task_id: UUID = Field(default_factory=uuid4)
    state: TaskState = Field(default=TaskState.init)
    os: OS
    config: TaskConfig
    error: Optional[Error]
    auth: Optional[Authentication]
    heartbeat: Optional[datetime]
    end_time: Optional[datetime]
    events: Optional[List[TaskEventSummary]]
    nodes: Optional[List[NodeAssignment]]
    user_info: Optional[UserInfo]
