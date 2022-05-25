// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use chrono::DateTime;
#[cfg(feature = "intel_instructions")]
use iced_x86::{Code as IntelInstructionCode, Mnemonic as IntelInstructionMnemonic};
use serde::{Deserialize, Serialize};
use std::fmt;
use std::sync::{LockResult, RwLockReadGuard, RwLockWriteGuard};
use uuid::Uuid;
#[cfg(feature = "z3")]
use z3_sys::ErrorCode as Z3ErrorCode;

pub use chrono::Utc;

pub use appinsights::telemetry::SeverityLevel::{Critical, Error, Information, Verbose, Warning};
use tokio::sync::broadcast::{self, Receiver};
#[macro_use]
extern crate lazy_static;

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

#[cfg(feature = "z3")]
pub fn z3_error_as_str(code: &Z3ErrorCode) -> &'static str {
    match code {
        Z3ErrorCode::OK => "OK",
        Z3ErrorCode::SortError => "SortError",
        Z3ErrorCode::IOB => "IOB",
        Z3ErrorCode::InvalidArg => "InvalidArg",
        Z3ErrorCode::ParserError => "ParserError",
        Z3ErrorCode::NoParser => "NoParser",
        Z3ErrorCode::InvalidPattern => "InvalidPattern",
        Z3ErrorCode::MemoutFail => "MemoutFail",
        Z3ErrorCode::FileAccessError => "FileAccessError",
        Z3ErrorCode::InternalFatal => "InternalFatal",
        Z3ErrorCode::InvalidUsage => "InvalidUsage",
        Z3ErrorCode::DecRefError => "DecRefError",
        Z3ErrorCode::Exception => "Exception",
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
    new_report,
    new_unique_report,
    new_unable_to_reproduce,
    regression_report,
    regression_unable_to_reproduce,
}

impl Event {
    pub fn as_str(&self) -> &'static str {
        match self {
            Self::task_start => "task_start",
            Self::coverage_data => "coverage_data",
            Self::new_coverage => "new_coverage",
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
    InputsFuzzed(u64),
    SatConstraints(u64),
    UnsatConstraints(u64),
    AverageVarsPerConstraint(u64),
    MaxConstraintVars(u64),
    AverageSymexTime(f64),
    MaxSymexTime(u64),
    AverageSolvingTime(f64),
    MaxSolvingTime(u64),
    UniqueCodeLocationCount(u64),
    AverageInstructionsExecuted(f64),
    MaxInstructionsExecuted(u64),
    AverageTaintedInstructions(f64),
    MaxTaintedInstructions(u64),
    AverageMemoryTaintedInstructions(f64),
    MaxMemoryTaintedInstructions(u64),
    AveragePathLength(f64),
    MaxPathLength(u64),
    DivergenceRate(f64),
    DivergencePathLength(u32),
    DivergencePathExpectedIndex(u32),
    DivergencePathActualIndex(u32),
    #[cfg(feature = "intel_instructions")]
    MissedInstructionCode(IntelInstructionCode),
    #[cfg(feature = "intel_instructions")]
    MissedInstructionMnemonic(IntelInstructionMnemonic),
    #[cfg(feature = "z3")]
    Z3ErrorCode(Z3ErrorCode),
    #[cfg(feature = "z3")]
    Z3ErrorString(String),
    SymexTimeout(u64),
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
            #[cfg(feature = "intel_instructions")]
            Self::MissedInstructionCode(x) => ("missed_instruction_code", format!("{:?}", x)),
            #[cfg(feature = "intel_instructions")]
            Self::MissedInstructionMnemonic(x) => {
                ("missed_instruction_mnemonic", format!("{:?}", x))
            }
            Self::InputsFuzzed(x) => ("inputs_fuzzed", x.to_string()),
            Self::SatConstraints(x) => ("sat_constraints", x.to_string()),
            Self::UnsatConstraints(x) => ("unsat_constraints", x.to_string()),
            Self::AverageVarsPerConstraint(x) => ("average_vars_per_constraint", x.to_string()),
            Self::MaxConstraintVars(x) => ("max_constraint_vars", x.to_string()),
            Self::AverageSymexTime(x) => ("average_symex_time", x.to_string()),
            Self::MaxSymexTime(x) => ("max_symex_time", x.to_string()),
            Self::AverageSolvingTime(x) => ("average_solving_time", x.to_string()),
            Self::MaxSolvingTime(x) => ("max_solving_time", x.to_string()),
            Self::UniqueCodeLocationCount(x) => ("unique_code_locations_count", x.to_string()),
            Self::AverageInstructionsExecuted(x) => {
                ("average_instructions_executed", x.to_string())
            }
            Self::MaxInstructionsExecuted(x) => ("max_instructions_executed", x.to_string()),
            Self::AverageTaintedInstructions(x) => ("average_tainted_instructions", x.to_string()),
            Self::MaxTaintedInstructions(x) => ("max_tainted_instructions", x.to_string()),
            Self::AverageMemoryTaintedInstructions(x) => {
                ("average_memory_tainted_instructions", x.to_string())
            }
            Self::MaxMemoryTaintedInstructions(x) => {
                ("max_memory_tainted_instructions", x.to_string())
            }
            Self::AveragePathLength(x) => ("average_path_length", x.to_string()),
            Self::MaxPathLength(x) => ("max_path_length", x.to_string()),
            Self::DivergenceRate(x) => ("divergence_rate", x.to_string()),
            Self::DivergencePathLength(x) => ("divergence_path_length", x.to_string()),
            Self::DivergencePathExpectedIndex(x) => {
                ("divergence_path_expected_index", x.to_string())
            }
            Self::DivergencePathActualIndex(x) => ("divergence_path_actual_index", x.to_string()),
            #[cfg(feature = "z3")]
            Self::Z3ErrorCode(x) => ("z3_error_code", z3_error_as_str(x).to_owned()),
            #[cfg(feature = "z3")]
            Self::Z3ErrorString(x) => ("z3_error_string", x.to_owned()),
            Self::SymexTimeout(x) => ("symex_timeout", x.to_string()),
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
            Self::InputsFuzzed(_) => true,
            Self::SatConstraints(_) => true,
            Self::UnsatConstraints(_) => true,
            Self::AverageVarsPerConstraint(_) => true,
            Self::MaxConstraintVars(_) => true,
            Self::AverageSymexTime(_) => true,
            Self::MaxSymexTime(_) => true,
            Self::AverageSolvingTime(_) => true,
            Self::MaxSolvingTime(_) => true,
            Self::UniqueCodeLocationCount(_) => true,
            Self::AverageInstructionsExecuted(_) => true,
            Self::MaxInstructionsExecuted(_) => true,
            Self::AverageTaintedInstructions(_) => true,
            Self::MaxTaintedInstructions(_) => true,
            Self::AverageMemoryTaintedInstructions(_) => true,
            Self::MaxMemoryTaintedInstructions(_) => true,
            Self::AveragePathLength(_) => true,
            Self::MaxPathLength(_) => true,
            Self::DivergenceRate(_) => true,
            Self::DivergencePathLength(_) => true,
            Self::DivergencePathExpectedIndex(_) => true,
            Self::DivergencePathActualIndex(_) => true,
            #[cfg(feature = "intel_instructions")]
            Self::MissedInstructionCode(_) => true,
            #[cfg(feature = "intel_instructions")]
            Self::MissedInstructionMnemonic(_) => true,
            #[cfg(feature = "z3")]
            Self::Z3ErrorCode(_) => true,
            #[cfg(feature = "z3")]
            Self::Z3ErrorString(_) => false,
            Self::SymexTimeout(_) => true,
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
        pub static ref EVENT_SOURCE: Sender<LoggingEvent> = {
            let (telemetry_event_source, _) = broadcast::channel::<_>(100);
            telemetry_event_source
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
pub fn set_appinsights_clients(
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

pub fn format_events(events: &[EventData]) -> String {
    events
        .iter()
        .map(|x| x.as_values())
        .map(|(x, y)| format!("{}:{}", x, y))
        .collect::<Vec<String>>()
        .join(" ")
}

fn try_broadcast_event(timestamp: &DateTime<Utc>, event: &Event, properties: &[EventData]) -> bool {
    // we ignore any send error here because they indicate that
    // there are no receivers on the other end
    let (timestamp, event, properties) = (*timestamp, event.clone(), properties.to_vec());
    global::EVENT_SOURCE
        .send(LoggingEvent::Event(LogEvent {
            timestamp,
            event,
            data: properties,
        }))
        .is_ok()
}

pub fn try_broadcast_trace(timestamp: DateTime<Utc>, msg: String, level: log::Level) -> bool {
    // we ignore any send error here because they indicate that
    // there are no receivers on the other end

    global::EVENT_SOURCE
        .send(LoggingEvent::Trace(LogTrace {
            timestamp,
            level,
            message: msg,
        }))
        .is_ok()
}

pub fn subscribe_to_events() -> Receiver<LoggingEvent> {
    global::EVENT_SOURCE.subscribe()
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
    try_broadcast_event(&chrono::Utc::now(), event, properties);
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

pub fn log_message(level: appinsights::telemetry::SeverityLevel, msg: String) {
    if let Some(client) = client(ClientType::Instance) {
        client.track_trace(msg, level);
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
macro_rules! log {
    ($level: expr, $($arg: tt)+) => {{
        let log_level = onefuzz_telemetry::to_log_level(&$level);
        if log_level <= log::max_level() {
            let msg = format!("{}", format_args!($($arg)+));
            log::log!(log_level, "{}", msg);
            onefuzz_telemetry::try_broadcast_trace(onefuzz_telemetry::Utc::now(), msg.to_string(), log_level);
            onefuzz_telemetry::log_message($level, msg.to_string());
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

#[macro_export]
macro_rules! metric {
    ($name: expr, $value: expr) => {{
        let client = onefuzz_telemetry::client(onefuzz_telemetry::ClientType::Instance);
        client.track_metric($name.into(), $value);
    }};
}
