#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from datetime import datetime
from typing import Any, Dict, Generic, List, Optional, TypeVar, Union
from uuid import UUID, uuid4

from pydantic import AnyHttpUrl, BaseModel, Field, root_validator, validator
from pydantic.dataclasses import dataclass

from ._monkeypatch import _check_hotfix
from .consts import ONE_HOUR, SEVEN_DAYS
from .enums import (
    OS,
    Architecture,
    Compare,
    ContainerPermission,
    ContainerType,
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
        try:
            self.secret = SecretAddress.parse_obj(secret)
        except Exception:
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
    # the code here is from ErrorCodes.cs, but we don't
    # want to validate the error code on the client-side
    code: int
    # a human-readable version of the error code
    title: str
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
    duration: int = Field(ge=ONE_HOUR, le=SEVEN_DAYS)
    logs: Optional[str]


class ReproConfig(BaseModel):
    container: Container
    path: str
    duration: int = Field(ge=ONE_HOUR, le=SEVEN_DAYS)


class TaskDetails(BaseModel):
    type: TaskType
    duration: int = Field(ge=ONE_HOUR, le=SEVEN_DAYS)
    target_exe: Optional[str]
    target_env: Optional[Dict[str, str]]
    target_options: Optional[List[str]]
    target_workers: Optional[int]
    target_options_merge: Optional[bool]
    check_asan_log: Optional[bool]
    check_debugger: Optional[bool] = Field(default=True)
    check_retry_count: Optional[int] = Field(ge=0)
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
    target_timeout: Optional[int] = Field(ge=1)
    ensemble_sync_delay: Optional[int]
    preserve_existing_outputs: Optional[bool]
    report_list: Optional[List[str]]
    minimized_stack_depth: Optional[int]
    coverage_filter: Optional[str]
    function_allowlist: Optional[str]
    module_allowlist: Optional[str]
    source_allowlist: Optional[str]
    target_assembly: Optional[str]
    target_class: Optional[str]
    target_method: Optional[str]
    task_env: Optional[Dict[str, str]]


class TaskPool(BaseModel):
    count: int
    pool_name: PoolName


class TaskVm(BaseModel):
    region: Region
    sku: str
    image: str
    count: int = Field(default=1, ge=0)
    spot_instances: bool = Field(default=False)
    reboot_after_setup: Optional[bool]


class TaskContainers(BaseModel):
    type: ContainerType
    name: Container


class TaskConfig(BaseModel):
    job_id: UUID
    prereq_tasks: Optional[List[UUID]]
    task: TaskDetails
    vm: Optional[TaskVm]
    pool: Optional[TaskPool]
    containers: Optional[List[TaskContainers]]
    tags: Optional[Dict[str, str]]
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
    tool_name: Optional[str]
    tool_version: Optional[str]
    onefuzz_version: Optional[str]
    scariness_score: Optional[int]
    scariness_description: Optional[str]
    minimized_stack: Optional[List[str]]
    minimized_stack_sha256: Optional[str]
    minimized_stack_function_names: Optional[List[str]]
    minimized_stack_function_names_sha256: Optional[str]
    minimized_stack_function_lines: Optional[List[str]]
    minimized_stack_function_lines_sha256: Optional[str]
    report_url: Optional[str]


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

    # validator needed to convert auth_token to SecretData
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

    # validator needed to convert url to SecretData
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
    resource: str
    tenant: str
    multi_tenant_domain: Optional[str]


class AgentConfig(BaseModel):
    client_credentials: Optional[ClientCredentials]
    onefuzz_url: str
    pool_name: PoolName
    heartbeat_queue: Optional[str]
    instance_telemetry_key: Optional[str]
    microsoft_telemetry_key: Optional[str]
    multi_tenant_domain: Optional[str]
    instance_id: UUID
    managed: Optional[bool] = Field(default=True)


class Forward(BaseModel):
    src_port: int
    dst_ip: str
    dst_port: int


class ProxyConfig(BaseModel):
    url: str
    notification: str
    region: Region
    proxy_id: UUID
    forwards: List[Forward]
    instance_telemetry_key: Optional[str]
    microsoft_telemetry_key: Optional[str]
    instance_id: UUID


class ProxyHeartbeat(BaseModel):
    region: Region
    proxy_id: UUID
    forwards: List[Forward]
    timestamp: datetime = Field(default_factory=datetime.utcnow)


class Files(BaseModel):
    files: List[str]


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
    user: str = Field(min_length=1)
    personal_access_token: str = Field(min_length=1)


class GithubIssueTemplate(BaseModel):
    auth: SecretData[GithubAuth]
    organization: str = Field(min_length=1)
    repository: str = Field(min_length=1)
    title: str = Field(min_length=1)
    body: str
    unique_search: GithubIssueSearch
    assignees: List[str]
    labels: List[str]
    on_duplicate: GithubIssueDuplicate

    # validator needed to convert auth to SecretData
    @validator("auth", pre=True, always=True)
    def validate_auth(cls, v: Any) -> SecretData:
        def try_parse_GithubAuth(x: dict) -> Optional[GithubAuth]:
            try:
                return GithubAuth.parse_obj(x)
            except Exception:
                return None

        if isinstance(v, GithubAuth):
            return SecretData(secret=v)
        elif isinstance(v, SecretData):
            return v
        elif isinstance(v, dict):
            githubAuth = try_parse_GithubAuth(v)
            if githubAuth:
                return SecretData(secret=githubAuth)

            githubAuth = try_parse_GithubAuth(v["secret"])
            if githubAuth:
                return SecretData(secret=githubAuth)

            return SecretData(secret=v["secret"])
        else:
            raise TypeError(f"invalid datatype {type(v)}")


NotificationTemplate = Union[ADOTemplate, TeamsTemplate, GithubIssueTemplate]


class Notification(BaseModel):
    timestamp: Optional[datetime] = Field(alias="Timestamp")
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


class NodeTasks(BaseModel):
    machine_id: UUID
    task_id: UUID
    state: NodeTaskState = Field(default=NodeTaskState.init)


class NodeCommandEnvelope(BaseModel):
    command: NodeCommand
    message_id: str


class Node(BaseModel):
    timestamp: Optional[datetime] = Field(alias="Timestamp")

    # Set only once, when a node is initialized.
    initialized_at: Optional[datetime]
    pool_name: PoolName
    pool_id: Optional[UUID]
    machine_id: UUID
    state: NodeState = Field(default=NodeState.init)
    scaleset_id: Optional[str] = None
    tasks: Optional[List[NodeTasks]] = None
    messages: Optional[List[NodeCommand]] = None
    heartbeat: Optional[datetime]
    version: str = Field(default="1.0.0")
    reimage_requested: bool = Field(default=False)
    delete_requested: bool = Field(default=False)
    debug_keep_node: bool = Field(default=False)


class ScalesetSummary(BaseModel):
    scaleset_id: str
    state: ScalesetState


class AutoScaleConfig(BaseModel):
    image: str
    max_size: int = Field(default=1000, le=1000, ge=0)  # max size of pool
    min_size: int = Field(default=0, le=1000, ge=0)  # min size of pool
    region: Optional[Region]
    scaleset_size: int  # Individual scaleset size
    spot_instances: bool = Field(default=False)
    ephemeral_os_disks: bool = Field(default=False)
    vm_sku: str

    @root_validator()
    def check_data(cls, values: Any) -> Any:
        if values["min_size"] > values["max_size"]:
            raise ValueError("The pool min_size is greater than max_size")
        return values


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
    object_id: Optional[UUID]

    # explicitly excluded from Tables
    scaleset_summary: Optional[List[ScalesetSummary]]


class ScalesetNodeState(BaseModel):
    machine_id: UUID
    instance_id: str
    state: Optional[NodeState]


class Scaleset(BaseModel):
    timestamp: Optional[datetime] = Field(alias="Timestamp")
    pool_name: PoolName
    scaleset_id: str
    state: ScalesetState = Field(default=ScalesetState.init)
    auth: Optional[Authentication]
    vm_sku: str
    image: str
    region: Region
    size: int = Field(ge=0)
    spot_instances: bool
    ephemeral_os_disks: bool = Field(default=False)
    needs_config_update: bool = Field(default=False)
    error: Optional[Error]
    nodes: Optional[List[ScalesetNodeState]]
    client_id: Optional[UUID]
    client_object_id: Optional[UUID]
    tags: Dict[str, str] = Field(default_factory=lambda: {})


class AutoScale(BaseModel):
    scaleset_id: str
    min: int = Field(ge=0)
    max: int = Field(ge=1)
    default: int = Field(ge=0)
    scale_out_amount: int = Field(ge=1)
    scale_out_cooldown: int = Field(ge=1)
    scale_in_amount: int = Field(ge=1)
    scale_in_cooldown: int = Field(ge=1)


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
    scaleset_id: Optional[str]
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


class NetworkConfig(BaseModel):
    address_space: str = Field(default="10.0.0.0/8")
    subnet: str = Field(default="10.0.0.0/16")


class NetworkSecurityGroupConfig(BaseModel):
    allowed_service_tags: List[str] = Field(default_factory=list)
    allowed_ips: List[str] = Field(default_factory=list)


class KeyvaultExtensionConfig(BaseModel):
    keyvault_name: str
    cert_name: str
    cert_path: str
    extension_store: str


class AzureMonitorExtensionConfig(BaseModel):
    config_version: str
    moniker: str
    namespace: str
    monitoringGSEnvironment: str
    monitoringGCSAccount: str
    monitoringGCSAuthId: str
    monitoringGCSAuthIdType: str


class AzureSecurityExtensionConfig(BaseModel):
    pass


class GenevaExtensionConfig(BaseModel):
    pass


class AzureVmExtensionConfig(BaseModel):
    keyvault: Optional[KeyvaultExtensionConfig]
    azure_monitor: Optional[AzureMonitorExtensionConfig]
    azure_security: Optional[AzureSecurityExtensionConfig]
    geneva: Optional[GenevaExtensionConfig]


class ApiAccessRule(BaseModel):
    methods: List[str]
    allowed_groups: List[UUID]


class TemplateRenderContext(BaseModel):
    report: Report
    task: TaskConfig
    job: JobConfig
    report_url: AnyHttpUrl
    input_url: AnyHttpUrl
    target_url: AnyHttpUrl
    report_container: Container
    report_filename: str
    repro_cmd: str


Endpoint = str
# json dumps doesn't support UUID as dictionary key
PrincipalID = str
GroupId = UUID


class InstanceConfig(BaseModel):
    # initial set of admins can only be set during deployment.
    # if admins are set, only admins can update instance configs.
    admins: Optional[List[UUID]] = None

    # if set, only admins can manage pools or scalesets
    require_admin_privileges: bool = Field(default=False)

    allowed_aad_tenants: List[UUID]
    network_config: NetworkConfig = Field(default_factory=NetworkConfig)
    proxy_nsg_config: NetworkSecurityGroupConfig = Field(
        default_factory=NetworkSecurityGroupConfig
    )
    extensions: Optional[AzureVmExtensionConfig]
    default_windows_vm_image: str = Field(
        default="MicrosoftWindowsDesktop:Windows-10:win10-21h2-pro:latest"
    )
    default_linux_vm_image: str = Field(
        default="Canonical:0001-com-ubuntu-server-focal:20_04-lts:latest"
    )
    proxy_vm_sku: str = Field(default="Standard_B2s")
    api_access_rules: Optional[Dict[Endpoint, ApiAccessRule]] = None
    group_membership: Optional[Dict[PrincipalID, List[GroupId]]] = None
    vm_tags: Optional[Dict[str, str]] = None
    vmss_tags: Optional[Dict[str, str]] = None

    def update(self, config: "InstanceConfig") -> None:
        for field in config.__fields__:
            # If no admins are set, then ignore setting admins
            if field == "admins" and self.admins is None:
                continue

            if hasattr(self, field):
                setattr(self, field, getattr(config, field))

    @validator("admins", allow_reuse=True)
    def check_admins(cls, value: Optional[List[UUID]]) -> Optional[List[UUID]]:
        if value is not None and len(value) == 0:
            raise ValueError("admins must be None or contain at least one UUID")
        return value

    # At the moment, this only checks allowed_aad_tenants, however adding
    # support for 3rd party JWT validation is anticipated in a future release.
    @root_validator()
    def check_instance_config(cls, values: Any) -> Any:
        if "allowed_aad_tenants" not in values:
            raise ValueError("missing allowed_aad_tenants")

        if not len(values["allowed_aad_tenants"]):
            raise ValueError("allowed_aad_tenants must not be empty")
        return values


_check_hotfix()
