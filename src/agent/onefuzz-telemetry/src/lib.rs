// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use serde::{Deserialize, Serialize};
use std::fmt;
use std::sync::{LockResult, RwLockReadGuard, RwLockWriteGuard};
use uuid::Uuid;

pub use appinsights::telemetry::SeverityLevel::{Critical, Error, Information, Verbose, Warning};

#[derive(Clone, Debug, Deserialize, Serialize, PartialEq, Eq)]
#[serde(transparent)]
pub struct MicrosoftTelemetryKey(Uuid);
impl MicrosoftTelemetryKey {
    pub fn new(value: Uuid) -> Self {
        Self(value)
    }
}

impl fmt::Display for MicrosoftTelemetryKey {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        write!(f, "{}", self.0)
    }
}

#[derive(Clone, Debug, Deserialize, Serialize, PartialEq, Eq)]
pub struct InstanceTelemetryKey(Uuid);
impl InstanceTelemetryKey {
    pub fn new(value: Uuid) -> Self {
        Self(value)
    }
}

impl fmt::Display for InstanceTelemetryKey {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        write!(f, "{}", self.0)
    }
}

pub type TelemetryClient = appinsights::TelemetryClient<appinsights::InMemoryChannel>;
pub enum ClientType {
    Instance,
    Microsoft,
}

#[derive(Clone, Debug, PartialEq)]
pub enum Role {
    Agent,
    Proxy,
    Supervisor,
}

impl Role {
    pub fn as_str(&self) -> &'static str {
        match self {
            Self::Agent => "agent",
            Self::Proxy => "proxy",
            Self::Supervisor => "supervisor",
        }
    }
}

#[allow(non_camel_case_types)]
#[derive(Clone, Debug)]
pub enum Event {
    task_start,
    coverage_data,
    new_result,
    new_coverage,
    runtime_stats,
    process_stats,
    new_report,
    new_unique_report,
    new_unable_to_reproduce,
}

impl Event {
    pub fn as_str(&self) -> &'static str {
        match self {
            Self::task_start => "task_start",
            Self::coverage_data => "coverage_data",
            Self::new_coverage => "new_coverage",
            Self::new_result => "new_result",
            Self::runtime_stats => "runtime_stats",
            Self::process_stats => "process_stats",
            Self::new_report => "new_report",
            Self::new_unique_report => "new_unique_report",
            Self::new_unable_to_reproduce => "new_unable_to_reproduce",
        }
    }
}

#[derive(Clone, Debug, PartialEq)]
pub enum EventData {
    WorkerId(u64),
    InstanceId(Uuid),
    JobId(Uuid),
    TaskId(Uuid),
    ScalesetId(String),
    MachineId(Uuid),
    Version(String),
    CommandLine(String),
    Type(String),
    Mode(String),
    Path(String),
    Features(u64),
    Covered(u64),
    Rate(f64),
    Count(u64),
    ExecsSecond(f64),
    RunId(Uuid),
    Name(String),
    Pid(u32),
    ProcessStatus(String),
    VirtualMemory(u64),
    PhysicalMemory(u64),
    CpuUsage(f32),
    Coverage(f64),
    CoveragePaths(u64),
    CoveragePathsFavored(u64),
    CoveragePathsFound(u64),
    CoveragePathsImported(u64),
    CoverageMaxDepth(u64),
    ToolName(String),
    Region(String),
    Role(Role),
}

impl EventData {
    pub fn as_values(&self) -> (&str, String) {
        match self {
            Self::Version(x) => ("version", x.to_string()),
            Self::InstanceId(x) => ("instance_id", x.to_string()),
            Self::JobId(x) => ("job_id", x.to_string()),
            Self::TaskId(x) => ("task_id", x.to_string()),
            Self::ScalesetId(x) => ("scaleset_id", x.to_string()),
            Self::MachineId(x) => ("machine_id", x.to_string()),
            Self::CommandLine(x) => ("command_line", x.to_owned()),
            Self::Type(x) => ("event_type", x.to_owned()),
            Self::Mode(x) => ("mode", x.to_owned()),
            Self::Path(x) => ("path", x.to_owned()),
            Self::Features(x) => ("features", x.to_string()),
            Self::Covered(x) => ("covered", x.to_string()),
            Self::Rate(x) => ("rate", x.to_string()),
            Self::Count(x) => ("count", x.to_string()),
            Self::ExecsSecond(x) => ("execs_sec", x.to_string()),
            Self::WorkerId(x) => ("worker_id", x.to_string()),
            Self::RunId(x) => ("run_id", x.to_string()),
            Self::Name(x) => ("name", x.to_owned()),
            Self::Pid(x) => ("pid", x.to_string()),
            Self::ProcessStatus(x) => ("process_status", x.to_string()),
            Self::VirtualMemory(x) => ("virtual_memory", x.to_string()),
            Self::PhysicalMemory(x) => ("physical_memory", x.to_string()),
            Self::CpuUsage(x) => ("cpu_usage", x.to_string()),
            Self::CoveragePaths(x) => ("coverage_paths", x.to_string()),
            Self::CoveragePathsFavored(x) => ("coverage_paths_favored", x.to_string()),
            Self::CoveragePathsFound(x) => ("coverage_paths_found", x.to_string()),
            Self::CoveragePathsImported(x) => ("coverage_paths_imported", x.to_string()),
            Self::CoverageMaxDepth(x) => ("coverage_paths_depth", x.to_string()),
            Self::Coverage(x) => ("coverage", x.to_string()),
            Self::ToolName(x) => ("tool_name", x.to_owned()),
            Self::Region(x) => ("region", x.to_owned()),
            Self::Role(x) => ("role", x.as_str().to_owned()),
        }
    }

    pub fn can_share_with_microsoft(&self) -> bool {
        match self {
            Self::Version(_) => true,
            Self::InstanceId(_) => true,
            Self::TaskId(_) => true,
            Self::JobId(_) => true,
            Self::MachineId(_) => true,
            Self::ScalesetId(_) => false,
            Self::CommandLine(_) => false,
            Self::Path(_) => false,
            Self::Type(_) => true,
            Self::Mode(_) => true,
            Self::Features(_) => true,
            Self::Covered(_) => true,
            Self::Rate(_) => true,
            Self::Count(_) => true,
            Self::ExecsSecond(_) => true,
            Self::WorkerId(_) => true,
            Self::RunId(_) => true,
            Self::Name(_) => false,
            Self::Pid(_) => false,
            Self::ProcessStatus(_) => false,
            Self::VirtualMemory(_) => true,
            Self::PhysicalMemory(_) => true,
            Self::CpuUsage(_) => true,
            Self::CoveragePaths(_) => true,
            Self::CoveragePathsFavored(_) => true,
            Self::CoveragePathsFound(_) => true,
            Self::CoveragePathsImported(_) => true,
            Self::CoverageMaxDepth(_) => true,
            Self::Coverage(_) => true,
            Self::ToolName(_) => true,
            Self::Region(_) => false,
            Self::Role(_) => true,
        }
    }
}

mod global {
    use std::sync::{
        atomic::{AtomicUsize, Ordering},
        RwLock,
    };

    use super::*;

    #[derive(Default)]
    pub struct Clients {
        instance: Option<RwLock<TelemetryClient>>,
        microsoft: Option<RwLock<TelemetryClient>>,
    }

    pub static mut CLIENTS: Clients = Clients {
        instance: None,
        microsoft: None,
    };
    const UNSET: usize = 0;
    const SETTING: usize = 1;
    const SET: usize = 2;

    static STATE: AtomicUsize = AtomicUsize::new(UNSET);

    pub fn set_clients(instance: Option<TelemetryClient>, microsoft: Option<TelemetryClient>) {
        use Ordering::SeqCst;

        let result = STATE.compare_exchange(UNSET, SETTING, SeqCst, SeqCst);

        match result {
            Ok(SETTING) => panic!("race while setting telemetry client"),
            Ok(SET) => panic!("tried to reset telemetry client"),
            Ok(UNSET) => {}
            Ok(state) => panic!("unknown telemetry client state while setting: {}", state),
            Err(state) => panic!("failed to set telemetry client state: {}", state),
        }

        unsafe {
            CLIENTS.instance = instance.map(RwLock::new);
            CLIENTS.microsoft = microsoft.map(RwLock::new);
        }

        STATE.store(SET, SeqCst);
    }

    pub fn client_lock(client_type: ClientType) -> Option<&'static RwLock<TelemetryClient>> {
        match client_type {
            ClientType::Instance => unsafe { CLIENTS.instance.as_ref() },
            ClientType::Microsoft => unsafe { CLIENTS.microsoft.as_ref() },
        }
    }

    pub fn take_clients() -> Vec<TelemetryClient> {
        use Ordering::SeqCst;

        let result = STATE.compare_exchange(SET, SETTING, SeqCst, SeqCst);

        match result {
            Ok(SETTING) => panic!("race while taking telemetry client"),
            Ok(SET) => {}
            Ok(UNSET) => panic!("tried to take unset telemetry client"),
            Ok(state) => panic!("unknown telemetry client state while taking: {}", state),
            Err(state) => panic!("failed to take telemetry client state: {}", state),
        }

        let instance = unsafe { CLIENTS.instance.take() };
        let microsoft = unsafe { CLIENTS.microsoft.take() };

        STATE.store(UNSET, SeqCst);

        let mut clients = Vec::new();

        for client in vec![instance, microsoft] {
            if let Some(client) = client {
                match client.into_inner() {
                    Ok(c) => clients.push(c),
                    Err(e) => panic!("Failed to extract telemetry client: {}", e),
                };
            }
        }
        clients
    }
}

pub fn set_appinsights_clients(
    instance_key: Option<InstanceTelemetryKey>,
    microsoft_key: Option<MicrosoftTelemetryKey>,
) {
    let instance_client = instance_key.map(|k| TelemetryClient::new(k.to_string()));
    let microsoft_client = microsoft_key.map(|k| TelemetryClient::new(k.to_string()));
    global::set_clients(instance_client, microsoft_client);
}

/// Try to submit any pending telemetry with a blocking call.
///
/// Meant for a final attempt at flushing pending items before an abnormal exit.
/// After calling this function, any existing telemetry client will be dropped,
/// and subsequent telemetry submission will be a silent no-op.
pub fn try_flush_and_close() {
    let clients = global::take_clients();

    for client in clients {
        client.flush_channel();
        client.close_channel();
    }
}

pub fn client(client_type: ClientType) -> Option<RwLockReadGuard<'static, TelemetryClient>> {
    Some(try_client(client_type)?.unwrap())
}

pub fn try_client(
    client_type: ClientType,
) -> Option<LockResult<RwLockReadGuard<'static, TelemetryClient>>> {
    let lock = global::client_lock(client_type)?;

    Some(lock.read())
}

pub fn client_mut(client_type: ClientType) -> Option<RwLockWriteGuard<'static, TelemetryClient>> {
    Some(try_client_mut(client_type)?.unwrap())
}

pub fn try_client_mut(
    client_type: ClientType,
) -> Option<LockResult<RwLockWriteGuard<'static, TelemetryClient>>> {
    let lock = global::client_lock(client_type)?;

    Some(lock.write())
}

pub fn property(client_type: ClientType, key: impl AsRef<str>) -> Option<String> {
    client(client_type).map(|c| {
        c.context()
            .properties()
            .get(key.as_ref())
            .map(|s| s.to_owned())
    })?
}

pub fn set_property(entry: EventData) {
    let (key, value) = entry.as_values();

    if entry.can_share_with_microsoft() {
        if let Some(mut client) = client_mut(ClientType::Microsoft) {
            client
                .context_mut()
                .properties_mut()
                .insert(key.to_owned(), value.to_owned());
        }
    }

    if let Some(mut client) = client_mut(ClientType::Instance) {
        client
            .context_mut()
            .properties_mut()
            .insert(key.to_owned(), value);
    }
}

fn local_log_event(event: &Event, properties: &[EventData]) {
    let as_values = properties
        .iter()
        .map(|x| x.as_values())
        .map(|(x, y)| format!("{}:{}", x, y))
        .collect::<Vec<String>>()
        .join(" ");
    log::log!(log::Level::Info, "{} {}", event.as_str(), as_values);
}

pub fn track_event(event: Event, properties: Vec<EventData>) {
    use appinsights::telemetry::Telemetry;

    if let Some(client) = client(ClientType::Instance) {
        let mut evt = appinsights::telemetry::EventTelemetry::new(event.as_str());
        let props = evt.properties_mut();
        for property in &properties {
            let (name, val) = property.as_values();
            props.insert(name.to_string(), val);
        }
        client.track(evt);
    }

    if let Some(client) = client(ClientType::Microsoft) {
        let mut evt = appinsights::telemetry::EventTelemetry::new(event.as_str());
        let props = evt.properties_mut();

        for property in &properties {
            if property.can_share_with_microsoft() {
                let (name, val) = property.as_values();
                props.insert(name.to_string(), val);
            }
        }
        client.track(evt);
    }
    local_log_event(&event, &properties);
}

pub fn to_log_level(level: &appinsights::telemetry::SeverityLevel) -> log::Level {
    match level {
        Verbose => log::Level::Debug,
        Information => log::Level::Info,
        Warning => log::Level::Warn,
        Error => log::Level::Error,
        Critical => log::Level::Error,
    }
}

pub fn should_log(level: &appinsights::telemetry::SeverityLevel) -> bool {
    to_log_level(level) <= log::max_level()
}

pub fn log_message(level: appinsights::telemetry::SeverityLevel, msg: String) {
    let log_level = to_log_level(&level);
    log::log!(log_level, "{}", msg);
    if let Some(client) = client(ClientType::Instance) {
        client.track_trace(msg, level);
    }
}

#[macro_export]
macro_rules! event {
    ($name: expr ; $($k: path = $v: expr),*) => {{
        let mut events = Vec::new();

        $({
            events.push($k(From::from($v)));

        })*;

        onefuzz_telemetry::track_event($name, events);
    }};
}

#[macro_export]
macro_rules! log {
    ($level: expr, $msg: expr) => {{
        if onefuzz_telemetry::should_log(&$level) {
            onefuzz_telemetry::log_message($level, $msg.to_string());
        }
    }};
}

#[macro_export]
macro_rules! debug {
    ($($tt: tt)*) => {{
        let msg = format!($($tt)*);
        onefuzz_telemetry::log!(onefuzz_telemetry::Verbose, msg);
    }}
}

#[macro_export]
macro_rules! info {
    ($($tt: tt)*) => {{
        let msg = format!($($tt)*);
        onefuzz_telemetry::log!(onefuzz_telemetry::Information, msg);
    }}
}

#[macro_export]
macro_rules! warn {
    ($($tt: tt)*) => {{
        let msg = format!($($tt)*);
        onefuzz_telemetry::log!(onefuzz_telemetry::Warning, msg);
    }}
}

#[macro_export]
macro_rules! error {
    ($($tt: tt)*) => {{
        let msg = format!($($tt)*);
        onefuzz_telemetry::log!(onefuzz_telemetry::Error, msg);
    }}
}

#[macro_export]
macro_rules! critical {
    ($($tt: tt)*) => {{
        let msg = format!($($tt)*);
        onefuzz_telemetry::log!(onefuzz_telemetry::Critical, msg);
    }}
}

#[macro_export]
macro_rules! metric {
    ($name: expr, $value: expr) => {{
        let client = onefuzz_telemetry::client(onefuzz_telemetry::ClientType::Instance);
        client.track_metric($name.into(), $value);
    }};
}
