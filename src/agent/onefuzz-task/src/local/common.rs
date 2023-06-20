use std::{
    collections::HashMap,
    env::current_dir,
    path::{Path, PathBuf},
    time::Duration,
};

use anyhow::Result;
use backoff::{future::retry, Error as BackoffError, ExponentialBackoff};
use clap::{Arg, ArgAction, ArgMatches, Command};
use flume::Sender;
use onefuzz::{
    blob::url::BlobContainerUrl, machine_id::MachineIdentity, monitor::DirectoryMonitor,
    syncdir::SyncedDir,
};
use path_absolutize::Absolutize;
use reqwest::Url;
use storage_queue::{local_queue::ChannelQueueClient, QueueClient};
use uuid::Uuid;

use crate::tasks::config::CommonConfig;
use crate::tasks::utils::parse_key_value;

pub const SETUP_DIR: &str = "setup_dir";
pub const INPUTS_DIR: &str = "inputs_dir";
pub const CRASHES_DIR: &str = "crashes_dir";
pub const TARGET_WORKERS: &str = "target_workers";
pub const REPORTS_DIR: &str = "reports_dir";
pub const NO_REPRO_DIR: &str = "no_repro_dir";
pub const TARGET_TIMEOUT: &str = "target_timeout";
pub const CHECK_RETRY_COUNT: &str = "check_retry_count";
pub const DISABLE_CHECK_QUEUE: &str = "disable_check_queue";
pub const UNIQUE_REPORTS_DIR: &str = "unique_reports_dir";
pub const COVERAGE_DIR: &str = "coverage_dir";
pub const READONLY_INPUTS: &str = "readonly_inputs_dir";
pub const CHECK_ASAN_LOG: &str = "check_asan_log";
pub const TOOLS_DIR: &str = "tools_dir";
pub const RENAME_OUTPUT: &str = "rename_output";
pub const CHECK_FUZZER_HELP: &str = "check_fuzzer_help";
pub const DISABLE_CHECK_DEBUGGER: &str = "disable_check_debugger";
pub const REGRESSION_REPORTS_DIR: &str = "regression_reports_dir";

pub const TARGET_EXE: &str = "target_exe";
pub const TARGET_ENV: &str = "target_env";
pub const TARGET_OPTIONS: &str = "target_options";
// pub const SUPERVISOR_EXE: &str = "supervisor_exe";
// pub const SUPERVISOR_ENV: &str = "supervisor_env";
// pub const SUPERVISOR_OPTIONS: &str = "supervisor_options";
pub const GENERATOR_EXE: &str = "generator_exe";
pub const GENERATOR_ENV: &str = "generator_env";
pub const GENERATOR_OPTIONS: &str = "generator_options";

pub const ANALYZER_EXE: &str = "analyzer_exe";
pub const ANALYZER_OPTIONS: &str = "analyzer_options";
pub const ANALYZER_ENV: &str = "analyzer_env";
pub const ANALYSIS_DIR: &str = "analysis_dir";
pub const ANALYSIS_INPUTS: &str = "analysis_inputs";
pub const ANALYSIS_UNIQUE_INPUTS: &str = "analysis_unique_inputs";
pub const PRESERVE_EXISTING_OUTPUTS: &str = "preserve_existing_outputs";

pub const CREATE_JOB_DIR: &str = "create_job_dir";

const WAIT_FOR_MAX_WAIT: Duration = Duration::from_secs(10);
const WAIT_FOR_DIR_DELAY: Duration = Duration::from_secs(1);

pub enum CmdType {
    Target,
    Generator,
    // Supervisor,
}

#[derive(Clone, Debug)]
pub struct LocalContext {
    pub job_path: PathBuf,
    pub common_config: CommonConfig,
    pub event_sender: Option<Sender<UiEvent>>,
}

pub fn get_hash_map(args: &clap::ArgMatches, name: &str) -> Result<HashMap<String, String>> {
    let mut env = HashMap::new();
    for opt in args.get_many::<String>(name).unwrap_or_default() {
        let (k, v) = parse_key_value(opt)?;
        env.insert(k, v);
    }
    Ok(env)
}

pub fn get_cmd_exe(cmd_type: CmdType, args: &clap::ArgMatches) -> Result<String> {
    let name = match cmd_type {
        CmdType::Target => TARGET_EXE,
        // CmdType::Supervisor => SUPERVISOR_EXE,
        CmdType::Generator => GENERATOR_EXE,
    };

    args.get_one::<String>(name)
        .cloned()
        .ok_or_else(|| format_err!("missing argument {name}"))
}

pub fn get_cmd_arg(cmd_type: CmdType, args: &clap::ArgMatches) -> Vec<String> {
    let name = match cmd_type {
        CmdType::Target => TARGET_OPTIONS,
        // CmdType::Supervisor => SUPERVISOR_OPTIONS,
        CmdType::Generator => GENERATOR_OPTIONS,
    };

    args.get_many::<String>(name)
        .unwrap_or_default()
        .cloned()
        .collect()
}

pub fn get_cmd_env(cmd_type: CmdType, args: &clap::ArgMatches) -> Result<HashMap<String, String>> {
    let env_name = match cmd_type {
        CmdType::Target => TARGET_ENV,
        // CmdType::Supervisor => SUPERVISOR_ENV,
        CmdType::Generator => GENERATOR_ENV,
    };
    get_hash_map(args, env_name)
}

pub fn add_common_config(app: Command) -> Command {
    app.arg(
        Arg::new("job_id")
            .long("job_id")
            .required(false)
            .value_parser(value_parser!(uuid::Uuid)),
    )
    .arg(
        Arg::new("task_id")
            .long("task_id")
            .required(false)
            .value_parser(value_parser!(uuid::Uuid)),
    )
    .arg(
        Arg::new("instance_id")
            .long("instance_id")
            .required(false)
            .value_parser(value_parser!(uuid::Uuid)),
    )
    .arg(
        Arg::new("setup_dir")
            .long("setup_dir")
            .required(false)
            .value_parser(value_parser!(PathBuf)),
    )
    .arg(
        Arg::new(CREATE_JOB_DIR)
            .long(CREATE_JOB_DIR)
            .action(ArgAction::SetTrue)
            .required(false)
            .help("create a local job directory to sync the files"),
    )
}

fn get_uuid(name: &str, args: &ArgMatches) -> Result<Uuid> {
    args.get_one::<Uuid>(name)
        .copied()
        .ok_or_else(|| format_err!("missing argument {name}"))
}

pub fn get_synced_dirs(
    name: &str,
    job_id: Uuid,
    task_id: Uuid,
    args: &ArgMatches,
) -> Result<Vec<SyncedDir>> {
    let create_job_dir = args.get_flag(CREATE_JOB_DIR);
    let current_dir = std::env::current_dir()?;
    args.get_many::<PathBuf>(name)
        .ok_or_else(|| anyhow!("argument '{}' not specified", name))?
        .enumerate()
        .map(|(index, path)| {
            if create_job_dir {
                let remote_path = path.absolutize()?;
                let remote_url = Url::from_file_path(remote_path).expect("invalid file path");
                let remote_blob_url = BlobContainerUrl::new(remote_url).expect("invalid url");
                let path = current_dir.join(format!("{job_id}/{task_id}/{name}_{index}"));
                Ok(SyncedDir {
                    remote_path: Some(remote_blob_url),
                    local_path: path,
                })
            } else {
                Ok(SyncedDir {
                    remote_path: None,
                    local_path: path.clone(),
                })
            }
        })
        .collect()
}

pub fn get_synced_dir(
    name: &str,
    job_id: Uuid,
    task_id: Uuid,
    args: &ArgMatches,
) -> Result<SyncedDir> {
    let remote_path = args
        .get_one::<PathBuf>(name)
        .ok_or_else(|| format_err!("missing argument {name}"))?
        .absolutize()?
        .into_owned();
    if args.get_flag(CREATE_JOB_DIR) {
        let remote_url =
            Url::from_file_path(remote_path).map_err(|_| anyhow!("invalid file path"))?;
        let remote_blob_url = BlobContainerUrl::new(remote_url)?;
        let path = std::env::current_dir()?.join(format!("{job_id}/{task_id}/{name}"));
        Ok(SyncedDir {
            remote_path: Some(remote_blob_url),
            local_path: path,
        })
    } else {
        Ok(SyncedDir {
            remote_path: None,
            local_path: remote_path,
        })
    }
}

// NOTE: generate_task_id is intended to change the default behavior for local
// fuzzing tasks from generating random task id to using UUID::nil(). This
// enables making the one-shot crash report generation, which isn't really a task,
// consistent across multiple runs.
pub async fn build_local_context(
    args: &ArgMatches,
    generate_task_id: bool,
    event_sender: Option<Sender<UiEvent>>,
) -> Result<LocalContext> {
    let job_id = get_uuid("job_id", args).unwrap_or_default();

    let task_id = get_uuid("task_id", args).unwrap_or_else(|_| {
        if generate_task_id {
            Uuid::new_v4()
        } else {
            Uuid::nil()
        }
    });

    let instance_id = get_uuid("instance_id", args).unwrap_or_default();

    let setup_dir = if let Some(setup_dir) = args.get_one::<PathBuf>(SETUP_DIR) {
        setup_dir.clone()
    } else if let Some(target_exe) = args.get_one::<String>(TARGET_EXE) {
        PathBuf::from(target_exe)
            .parent()
            .map(|x| x.to_path_buf())
            .unwrap_or_default()
    } else {
        PathBuf::default()
    };

    let common_config = CommonConfig {
        job_id,
        task_id,
        instance_id,
        setup_dir,
        extra_setup_dir: None,
        extra_output: None,
        machine_identity: MachineIdentity {
            machine_id: Uuid::nil(),
            machine_name: "local".to_string(),
            scaleset_name: None,
        },
        instance_telemetry_key: None,
        heartbeat_queue: None,
        microsoft_telemetry_key: None,
        logs: None,
        min_available_memory_mb: 0,
        tags: Default::default(),
        from_agent_to_task_endpoint: "/".to_string(),
        from_task_to_agent_endpoint: "/".to_string(),
    };

    let current_dir = current_dir()?;
    let job_path = current_dir.join(format!("{job_id}"));
    Ok(LocalContext {
        job_path,
        common_config,
        event_sender,
    })
}

/// Information about a local path being monitored
/// A new notification will be received on the queue url
/// For each new file added to the directory
pub struct DirectoryMonitorQueue {
    pub directory_path: PathBuf,
    pub queue_client: storage_queue::QueueClient,
    pub handle: tokio::task::JoinHandle<Result<()>>,
}

impl DirectoryMonitorQueue {
    pub async fn start_monitoring(directory_path: impl AsRef<Path>) -> Result<Self> {
        let directory_path = directory_path.as_ref().to_owned();
        let queue_client = QueueClient::Channel(ChannelQueueClient::new()?);
        let monitor_task = monitor_directory(queue_client.clone(), directory_path.clone());
        let handle = tokio::spawn(monitor_task);

        Ok(DirectoryMonitorQueue {
            directory_path,
            queue_client,
            handle,
        })
    }
}

async fn monitor_directory(queue_client: QueueClient, directory: PathBuf) -> Result<()> {
    let mut monitor = DirectoryMonitor::new(&directory).await?;

    while let Some(file_path) = monitor.next_file().await? {
        let file_url = Url::from_file_path(file_path).map_err(|_| anyhow!("invalid file path"))?;

        queue_client.enqueue(file_url).await?;
    }

    Ok(())
}

pub async fn wait_for_dir(path: impl AsRef<Path>) -> Result<()> {
    let op = || async {
        if path.as_ref().exists() {
            Ok(())
        } else {
            Err(BackoffError::transient(anyhow::anyhow!(
                "path '{}' does not exist",
                path.as_ref().display()
            )))
        }
    };
    retry(
        ExponentialBackoff {
            max_elapsed_time: Some(WAIT_FOR_MAX_WAIT),
            max_interval: WAIT_FOR_DIR_DELAY,
            ..ExponentialBackoff::default()
        },
        op,
    )
    .await
}

#[derive(Debug)]
pub enum UiEvent {
    MonitorDir(PathBuf),
}

pub trait SyncCountDirMonitor<T: Sized> {
    fn monitor_count(self, event_sender: &Option<Sender<UiEvent>>) -> Result<T>;
}

impl SyncCountDirMonitor<SyncedDir> for SyncedDir {
    fn monitor_count(self, event_sender: &Option<Sender<UiEvent>>) -> Result<Self> {
        if let (Some(event_sender), Some(p)) = (event_sender, self.remote_url()?.as_file_path()) {
            event_sender.send(UiEvent::MonitorDir(p))?;
        }
        Ok(self)
    }
}

impl SyncCountDirMonitor<Option<SyncedDir>> for Option<SyncedDir> {
    fn monitor_count(self, event_sender: &Option<Sender<UiEvent>>) -> Result<Self> {
        if let Some(sd) = self {
            let sd = sd.monitor_count(event_sender)?;
            Ok(Some(sd))
        } else {
            Ok(self)
        }
    }
}
