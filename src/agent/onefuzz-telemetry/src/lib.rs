// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use chrono::DateTime;
use serde::{Deserialize, Serialize};
use std::fmt;
use std::sync::{LockResult, RwLockReadGuard, RwLockWriteGuard};
use std::time::Duration;
use uuid::Uuid;

pub use chrono::Utc;

use anyhow::{bail, Result};
pub use appinsights::telemetry::SeverityLevel::{Critical, Error, Information, Verbose, Warning};
use tokio::sync::broadcast::{self, Receiver};
#[macro_use]
extern crate lazy_static;

const DEAFAULT_CHANNEL_CLOSING_TIMEOUT: Duration = Duration::from_secs(30);

#[derive(Clone, Debug, Deserialize, Serialize, PartialEq, Eq)]
#[serde(transparent)]
pub struct MicrosoftTelemetryKey(Uuid);
impl MicrosoftTelemetryKey {
    pub fn new(value: Uuid) -> Self {
        Self(value)
    }
}

impl fmt::Display for MicrosoftTelemetryKey {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
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
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{}", self.0)
    }
}

pub type TelemetryClient = appinsights::TelemetryClient;
pub enum ClientType {
    Instance,
    Microsoft,
}

#[derive(Clone, Debug, PartialEq, Eq)]
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
    coverage_failed,
    new_result,
    new_crashdump,
    new_coverage,
    runtime_stats,
    new_report,
    new_unique_report,
    crash_reported,
    new_unable_to_reproduce,
    regression_report,
    regression_unable_to_reproduce,
}

impl Event {
    pub fn as_str(&self) -> &'static str {
        match self {
            Self::task_start => "task_start",
            Self::coverage_data => "coverage_data",
            Self::coverage_failed => "coverage_failed",
            Self::new_coverage => "new_coverage",
            Self::new_crashdump => "new_crashdump",
            Self::new_result => "new_result",
            Self::runtime_stats => "runtime_stats",
            Self::new_report => "new_report",
            Self::new_unique_report => "new_unique_report",
            Self::new_unable_to_reproduce => "new_unable_to_reproduce",
            Self::regression_report => "regression_report",
            Self::regression_unable_to_reproduce => "regression_unable_to_reproduce",
        }
    }
}

#[derive(Clone, Debug, PartialEq)]
pub enum EventData {
    WorkerId(usize),
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

#[derive(Clone, Debug)]
pub enum LoggingEvent {
    Trace(LogTrace),
    Event(LogEvent),
}

#[derive(Clone, Debug)]
pub struct LogTrace {
    pub timestamp: DateTime<Utc>,
    pub level: log::Level,
    pub message: String,
}

#[derive(Clone, Debug)]
pub struct LogEvent {
    pub timestamp: DateTime<Utc>,
    pub event: Event,
    pub data: Vec<EventData>,
}

mod global {
    use std::sync::{
        atomic::{AtomicUsize, Ordering},
        RwLock,
    };

    use tokio::sync::broadcast::Sender;

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

    lazy_static! {
        pub static ref EVENT_SOURCE: RwLock<Option<Sender<LoggingEvent>>> = {
            let (telemetry_event_source, _) = broadcast::channel::<_>(5000);
            RwLock::new(Some(telemetry_event_source))
        };
    }

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

        for client in vec![instance, microsoft].into_iter().flatten() {
            match client.into_inner() {
                Ok(c) => clients.push(c),
                Err(e) => panic!("Failed to extract telemetry client: {}", e),
            };
        }
        clients
    }
}

const REDACTED: &str = "Redacted";
// This function doesn't do anything async, but TelemetryClient::new must be invoked
// upon a Tokio runtime task, since it calls Tokio::spawn. The easiest way to ensure this
// statically is to make this function async.
pub async fn set_appinsights_clients(
    instance_key: Option<InstanceTelemetryKey>,
    microsoft_key: Option<MicrosoftTelemetryKey>,
) {
    let instance_client = instance_key.map(|k| TelemetryClient::new(k.to_string()));
    let microsoft_client = microsoft_key.map(|k| {
        let mut tc = TelemetryClient::new(k.to_string());
        //Redact IP and machine name from telemetry
        tc.context_mut()
            .tags_mut()
            .location_mut()
            .set_ip("0.0.0.0".to_string());
        tc.context_mut()
            .tags_mut()
            .cloud_mut()
            .set_role_instance(REDACTED.to_string());
        tc.context_mut()
            .tags_mut()
            .device_mut()
            .set_id(REDACTED.to_string());
        tc
    });

    global::set_clients(instance_client, microsoft_client);
}

pub async fn try_flush_and_close() {
    _try_flush_and_close(DEAFAULT_CHANNEL_CLOSING_TIMEOUT).await
}

/// Try to submit any pending telemetry with a blocking call.
///
/// Meant for a final attempt at flushing pending items before an abnormal exit.
/// After calling this function, any existing telemetry client will be dropped,
/// and subsequent telemetry submission will be a silent no-op.
pub async fn _try_flush_and_close(timeout: Duration) {
    let clients = global::take_clients();
    for client in clients {
        if let Err(e) = tokio::time::timeout(timeout, client.close_channel()).await {
            log::warn!("Failed to close telemetry client: {}", e);
        }
    }
    // dropping the broadcast sender to make sure all pending events are sent
    let _global_event_source = global::EVENT_SOURCE.write().unwrap().take();
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

pub fn format_events(events: &[EventData]) -> String {
    events
        .iter()
        .map(|x| x.as_values())
        .map(|(x, y)| format!("{x}:{y}"))
        .collect::<Vec<String>>()
        .join(" ")
}

fn try_broadcast_event(timestamp: DateTime<Utc>, event: &Event, properties: &[EventData]) -> bool {
    // we ignore any send error here because they indicate that
    // there are no receivers on the other end

    if let Some(ev) = global::EVENT_SOURCE.read().ok().and_then(|f| f.clone()) {
        let (event, properties) = (event.clone(), properties.to_vec());

        return ev
            .send(LoggingEvent::Event(LogEvent {
                timestamp,
                event,
                data: properties,
            }))
            .is_ok();
    }

    false
}

pub fn try_broadcast_trace(timestamp: DateTime<Utc>, msg: String, level: log::Level) -> bool {
    // we ignore any send error here because they indicate that
    // there are no receivers on the other end
    if let Some(ev) = global::EVENT_SOURCE.read().ok().and_then(|f| f.clone()) {
        return ev
            .send(LoggingEvent::Trace(LogTrace {
                timestamp,
                level,
                message: msg,
            }))
            .is_ok();
    }
    false
}

pub fn subscribe_to_events() -> Result<Receiver<LoggingEvent>> {
    match global::EVENT_SOURCE.read() {
        Ok(global_event_source) => {
            if let Some(evs) = global_event_source.clone() {
                Ok(evs.subscribe())
            } else {
                bail!("Event source not initialized");
            }
        }
        Err(e) => bail!("failed to acquire event source lock: {}", e),
    }
}

pub fn track_event(event: &Event, properties: &[EventData]) {
    use appinsights::telemetry::Telemetry;

    if let Some(client) = client(ClientType::Instance) {
        let mut evt = appinsights::telemetry::EventTelemetry::new(event.as_str());
        let props = evt.properties_mut();
        for property in properties {
            let (name, val) = property.as_values();
            props.insert(name.to_string(), val);
        }
        client.track(evt);
    }

    if let Some(client) = client(ClientType::Microsoft) {
        let mut evt = appinsights::telemetry::EventTelemetry::new(event.as_str());
        let props = evt.properties_mut();

        for property in properties {
            if property.can_share_with_microsoft() {
                let (name, val) = property.as_values();
                props.insert(name.to_string(), val);
            }
        }
        client.track(evt);
    }
    try_broadcast_event(chrono::Utc::now(), event, properties);
}

pub fn track_metric(metric: &Event, value: f64, properties: &[EventData]) {
    use appinsights::telemetry::Telemetry;

    if let Some(client) = client(ClientType::Instance) {
        let mut mtr = appinsights::telemetry::MetricTelemetry::new(metric.as_str(), value);
        let props = mtr.properties_mut();
        for property in properties {
            let (name, val) = property.as_values();
            props.insert(name.to_string(), val);
        }
        client.track(mtr);
    }

    if let Some(client) = client(ClientType::Microsoft) {
        let mut mtr = appinsights::telemetry::MetricTelemetry::new(metric.as_str(), value);
        let props = mtr.properties_mut();

        for property in properties {
            if property.can_share_with_microsoft() {
                let (name, val) = property.as_values();
                props.insert(name.to_string(), val);
            }
        }
        client.track(mtr);
    }
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

#[macro_export]
macro_rules! log_events {
    ($name: expr; $events: expr) => {{
        onefuzz_telemetry::track_event(&$name, &$events);
        log::info!(
            "{} {}",
            $name.as_str(),
            onefuzz_telemetry::format_events(&$events)
        );
    }};
}

#[macro_export]
macro_rules! event {
    ($name: expr ; $($k: path = $v: expr),*) => {{
        let mut events = Vec::new();

        $({
            events.push($k(From::from($v)));

        })*;

        log_events!($name; events);
    }};
}

#[macro_export]
macro_rules! log_metrics {
    ($name: expr; $value: expr; $metrics: expr) => {{
        onefuzz_telemetry::track_metric(&$name, $value, &$metrics);
    }};
}

#[macro_export]
macro_rules! metric {
    ($name: expr ; $value: expr ; $($k: path = $v: expr),*) => {{
        let mut metrics = Vec::new();

        $({
            metrics.push($k(From::from($v)));

        })*;

        log_metrics!($name; $value; metrics);
    }};
}

#[macro_export]
macro_rules! log {
    ($level: expr, $($arg: tt)+) => {{
        let log_level = onefuzz_telemetry::to_log_level(&$level);
        if log_level <= log::max_level() {
            let msg = format!("{}", format_args!($($arg)+));
            log::log!(log_level, "{}", msg);
            onefuzz_telemetry::try_broadcast_trace(onefuzz_telemetry::Utc::now(), msg.to_string(), log_level);
        }
    }};
}

#[macro_export]
macro_rules! debug {
    ($($arg: tt)+) => {{
        onefuzz_telemetry::log!(onefuzz_telemetry::Verbose, $($arg)+);
    }}
}

#[macro_export]
macro_rules! info {
    ($($arg: tt)+) => {{
        onefuzz_telemetry::log!(onefuzz_telemetry::Information, $($arg)+);
    }}
}

#[macro_export]
macro_rules! warn {
    ($($arg: tt)+) => {{
        onefuzz_telemetry::log!(onefuzz_telemetry::Warning, $($arg)+);
    }}
}

#[macro_export]
macro_rules! error {
    ($($arg: tt)+) => {{
        onefuzz_telemetry::log!(onefuzz_telemetry::Error, $($arg)+);
    }}
}

#[macro_export]
macro_rules! critical {
    ($($arg: tt)+) => {{
        onefuzz_telemetry::log!(onefuzz_telemetry::Critical, $($arg)+);
    }}
}
