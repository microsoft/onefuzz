// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::sync::{LockResult, RwLockReadGuard, RwLockWriteGuard};
use uuid::Uuid;

pub type TelemetryClient = appinsights::TelemetryClient<appinsights::InMemoryChannel>;
pub enum ClientType {
    Instance,
    Shared,
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
        }
    }
}

#[derive(Clone, Debug, PartialEq)]
pub enum EventData {
    WorkerId(u64),
    JobId(Uuid),
    TaskId(Uuid),
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
}

impl EventData {
    pub fn as_values(&self) -> (&str, String) {
        match self {
            Self::Version(x) => ("version", x.to_string()),
            Self::JobId(x) => ("job_id", x.to_string()),
            Self::TaskId(x) => ("task_id", x.to_string()),
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
        }
    }

    pub fn can_share(&self) -> bool {
        match self {
            // TODO: Request CELA review of Version, as having this for central stats
            //       would be useful to track uptake of new releases
            Self::Version(_) => false,
            Self::TaskId(_) => true,
            Self::JobId(_) => true,
            Self::MachineId(_) => true,
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
        shared: Option<RwLock<TelemetryClient>>,
    }

    pub static mut CLIENTS: Clients = Clients {
        instance: None,
        shared: None,
    };
    const UNSET: usize = 0;
    const SETTING: usize = 1;
    const SET: usize = 2;

    static STATE: AtomicUsize = AtomicUsize::new(UNSET);

    pub fn set_clients(instance: TelemetryClient, shared: TelemetryClient) {
        use Ordering::SeqCst;

        let last_state = STATE.compare_and_swap(UNSET, SETTING, SeqCst);

        if last_state == SETTING {
            panic!("race while setting telemetry client");
        }

        if last_state == SET {
            panic!("tried to reset telemetry client");
        }

        assert_eq!(last_state, UNSET, "unexpected telemetry client state");

        unsafe {
            CLIENTS.instance = Some(RwLock::new(instance));
            CLIENTS.shared = Some(RwLock::new(shared));
        };

        STATE.store(SET, SeqCst);
    }

    pub fn client_lock(client_type: ClientType) -> Option<&'static RwLock<TelemetryClient>> {
        match client_type {
            ClientType::Instance => unsafe { CLIENTS.instance.as_ref() },
            ClientType::Shared => unsafe { CLIENTS.shared.as_ref() },
        }
    }

    pub fn take_clients() -> Vec<TelemetryClient> {
        use Ordering::SeqCst;

        let last_state = STATE.compare_and_swap(SET, SETTING, SeqCst);

        if last_state == SETTING {
            panic!("race while taking telemetry client");
        }

        if last_state == UNSET {
            panic!("tried to take unset telemetry client");
        }

        assert_eq!(last_state, SET, "unexpected telemetry client state");

        let instance = unsafe { CLIENTS.instance.take() };
        let shared = unsafe { CLIENTS.shared.take() };

        STATE.store(UNSET, SeqCst);

        let mut clients = Vec::new();

        for client in vec![instance, shared] {
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

pub fn set_appinsights_clients(ikey: impl Into<String>, tkey: impl Into<String>) {
    let instance_client = TelemetryClient::new(ikey.into());
    let shared_client = TelemetryClient::new(tkey.into());

    global::set_clients(instance_client, shared_client);
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
    let key = key.as_ref();

    let client = client(client_type).expect("telemetry client called internally when unset");

    Some(client.context().properties().get(key)?.to_owned())
}

pub fn set_property(entry: EventData) {
    let (key, value) = entry.as_values();

    if entry.can_share() {
        let mut client =
            client_mut(ClientType::Shared).expect("telemetry client called internally when unset");
        client
            .context_mut()
            .properties_mut()
            .insert(key.to_owned(), value.to_owned());
    }

    let mut client =
        client_mut(ClientType::Instance).expect("telemetry client called internally when unset");
    client
        .context_mut()
        .properties_mut()
        .insert(key.to_owned(), value);
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

    if let Some(client) = client(ClientType::Shared) {
        let mut evt = appinsights::telemetry::EventTelemetry::new(event.as_str());
        let props = evt.properties_mut();

        for property in &properties {
            if property.can_share() {
                let (name, val) = property.as_values();
                props.insert(name.to_string(), val);
            }
        }
        client.track(evt);
    }
}

#[macro_export]
macro_rules! event {
    ($name: expr ; $($k: path = $v: expr),*) => {{
        let mut events = Vec::new();

        $({
            events.push($k(From::from($v)));

        })*;

        $crate::telemetry::track_event($name, events);
    }};
}

#[macro_export]
macro_rules! log {
    ($level: expr, $msg: expr) => {{
        use appinsights::telemetry::SeverityLevel;
        use SeverityLevel::*;

        {
            let log_level = match $level {
                SeverityLevel::Verbose => log::Level::Debug,
                SeverityLevel::Information => log::Level::Info,
                SeverityLevel::Warning => log::Level::Warn,
                SeverityLevel::Error => log::Level::Error,
                SeverityLevel::Critical => log::Level::Error,
            };

            let log_msg = $msg.to_string();

            log::log!(log_level, "{}", log_msg)
        }

        if let Some(client) = $crate::telemetry::client($crate::telemetry::ClientType::Instance) {
            client.track_trace($msg, $level);
        }
    }};
}

#[macro_export]
macro_rules! verbose {
    ($($tt: tt)*) => {{
        let msg = format!($($tt)*);
        $crate::log!(Verbose, msg);
    }}
}

#[macro_export]
macro_rules! info {
    ($($tt: tt)*) => {{
        let msg = format!($($tt)*);
        $crate::log!(Information, msg);
    }}
}

#[macro_export]
macro_rules! warn {
    ($($tt: tt)*) => {{
        let msg = format!($($tt)*);
        $crate::log!(Warning, msg);
    }}
}

#[macro_export]
macro_rules! error {
    ($($tt: tt)*) => {{
        let msg = format!($($tt)*);
        $crate::log!(Error, msg);
    }}
}

#[macro_export]
macro_rules! critical {
    ($($tt: tt)*) => {{
        let msg = format!($($tt)*);
        $crate::log!(Critical, msg);
    }}
}

#[macro_export]
macro_rules! metric {
    ($name: expr, $value: expr) => {{
        let client = $crate::telemetry::client($crate::telemetry::ClientType::Instance);
        client.track_metric($name.into(), $value);
    }};
}
