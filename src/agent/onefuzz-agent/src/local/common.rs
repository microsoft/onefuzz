use crate::tasks::config::CommonConfig;
use crate::tasks::utils::parse_key_value;
use anyhow::Result;
use backoff::{future::retry, Error as BackoffError, ExponentialBackoff};
use clap::{App, Arg, ArgMatches};
use flume::Sender;
use onefuzz::{blob::url::BlobContainerUrl, monitor::DirectoryMonitor, syncdir::SyncedDir};
use path_absolutize::Absolutize;
use reqwest::Url;
use std::{
    collections::HashMap,
    env::current_dir,
    path::{Path, PathBuf},
    time::Duration,
};
use uuid::Uuid;

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

#[cfg(any(target_os = "linux", target_os = "windows"))]
pub const COVERAGE_FILTER: &str = "coverage_filter";

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

pub fn get_hash_map(args: &clap::ArgMatches<'_>, name: &str) -> Result<HashMap<String, String>> {
    let mut env = HashMap::new();
    for opt in args.values_of_lossy(name).unwrap_or_default() {
        let (k, v) = parse_key_value(opt)?;
        env.insert(k, v);
    }
    Ok(env)
}

pub fn get_cmd_exe(cmd_type: CmdType, args: &clap::ArgMatches<'_>) -> Result<String> {
    let name = match cmd_type {
        CmdType::Target => TARGET_EXE,
        // CmdType::Supervisor => SUPERVISOR_EXE,
        CmdType::Generator => GENERATOR_EXE,
    };

    let exe = value_t!(args, name, String)?;
    Ok(exe)
}

pub fn get_cmd_arg(cmd_type: CmdType, args: &clap::ArgMatches<'_>) -> Vec<String> {
    let name = match cmd_type {
        CmdType::Target => TARGET_OPTIONS,
        // CmdType::Supervisor => SUPERVISOR_OPTIONS,
        CmdType::Generator => GENERATOR_OPTIONS,
    };

    args.values_of_lossy(name).unwrap_or_default()
}

pub fn get_cmd_env(
    cmd_type: CmdType,
    args: &clap::ArgMatches<'_>,
) -> Result<HashMap<String, String>> {
    let env_name = match cmd_type {
        CmdType::Target => TARGET_ENV,
        // CmdType::Supervisor => SUPERVISOR_ENV,
        CmdType::Generator => GENERATOR_ENV,
    };
    get_hash_map(args, env_name)
}

pub fn add_common_config(app: App<'static, 'static>) -> App<'static, 'static> {
    app.arg(
        Arg::with_name("job_id")
            .long("job_id")
            .takes_value(true)
            .required(false),
    )
    .arg(
        Arg::with_name("task_id")
            .long("task_id")
            .takes_value(true)
            .required(false),
    )
    .arg(
        Arg::with_name("instance_id")
            .long("instance_id")
            .takes_value(true)
            .required(false),
    )
    .arg(
        Arg::with_name("setup_dir")
            .long("setup_dir")
            .takes_value(true)
            .required(false),
    )
    .arg(
        Arg::with_name(CREATE_JOB_DIR)
            .long(CREATE_JOB_DIR)
            .required(false)
            .help("create a local job directory to sync the files"),
    )
}

fn get_uuid(name: &str, args: &ArgMatches<'_>) -> Result<Uuid> {
    value_t!(args, name, String).map(|x| {
        Uuid::parse_str(&x).map_err(|x| format_err!("invalid {}.  uuid expected.  {})", name, x))
    })?
}

pub fn get_synced_dirs(
    name: &str,
    job_id: Uuid,
    task_id: Uuid,
    args: &ArgMatches<'_>,
) -> Result<Vec<SyncedDir>> {
    let create_job_dir = args.is_present(CREATE_JOB_DIR);
    let current_dir = std::env::current_dir()?;
    args.values_of_os(name)
        .ok_or_else(|| anyhow!("argument '{}' not specified", name))?
        .enumerate()
        .map(|(index, remote_path)| {
            let path = PathBuf::from(remote_path);
            if create_job_dir {
                let remote_path = path.absolutize()?;
                let remote_url = Url::from_file_path(remote_path).expect("invalid file path");
                let remote_blob_url = BlobContainerUrl::new(remote_url).expect("invalid url");
                let path = current_dir.join(format!("{}/{}/{}_{}", job_id, task_id, name, index));
                Ok(SyncedDir {
                    remote_path: Some(remote_blob_url),
                    local_path: path,
                })
            } else {
                Ok(SyncedDir {
                    remote_path: None,
                    local_path: path,
                })
            }
        })
        .collect()
}

pub fn get_synced_dir(
    name: &str,
    job_id: Uuid,
    task_id: Uuid,
    args: &ArgMatches<'_>,
) -> Result<SyncedDir> {
    let remote_path = value_t!(args, name, PathBuf)?.absolutize()?.into_owned();
    if args.is_present(CREATE_JOB_DIR) {
        let remote_url =
            Url::from_file_path(remote_path).map_err(|_| anyhow!("invalid file path"))?;
        let remote_blob_url = BlobContainerUrl::new(remote_url)?;
        let path = std::env::current_dir()?.join(format!("{}/{}/{}", job_id, task_id, name));
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
pub fn build_local_context(
    args: &ArgMatches<'_>,
    generate_task_id: bool,
    event_sender: Option<Sender<UiEvent>>,
) -> Result<LocalContext> {
    let job_id = get_uuid("job_id", args).unwrap_or_else(|_| Uuid::nil());
    let task_id = get_uuid("task_id", args).unwrap_or_else(|_| {
        if generate_task_id {
            Uuid::new_v4()
        } else {
            Uuid::nil()
        }
    });
    let instance_id = get_uuid("instance_id", args).unwrap_or_else(|_| Uuid::nil());

    let setup_dir = if args.is_present(SETUP_DIR) {
        value_t!(args, SETUP_DIR, PathBuf)?
    } else if args.is_present(TARGET_EXE) {
        value_t!(args, TARGET_EXE, PathBuf)?
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
        ..Default::default()
    };
    let current_dir = current_dir()?;
    let job_path = current_dir.join(format!("{}", job_id));
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
        let directory_path = PathBuf::from(directory_path.as_ref());
        let directory_path_clone = directory_path.clone();
        let queue_client = storage_queue::QueueClient::Channel(
            storage_queue::local_queue::ChannelQueueClient::new()?,
        );
        let queue = queue_client.clone();
        let handle: tokio::task::JoinHandle<Result<()>> = tokio::spawn(async move {
            let mut monitor = DirectoryMonitor::new(directory_path_clone.clone());
            monitor.start()?;

            while let Some(file_path) = monitor.next_file().await {
                let file_url =
                    Url::from_file_path(file_path).map_err(|_| anyhow!("invalid file path"))?;
                queue.enqueue(file_url).await?;
            }

            Ok(())
        });

        Ok(DirectoryMonitorQueue {
            directory_path,
            queue_client,
            handle,
        })
    }
}

pub async fn wait_for_dir(path: impl AsRef<Path>) -> Result<()> {
    let op = || async {
        if path.as_ref().exists() {
            Ok(())
        } else {
            let max_retry_period: Option<Duration> = Some(WAIT_FOR_MAX_WAIT);
            Err(BackoffError::Transient {
                err: anyhow::anyhow!("path '{}' does not exist", path.as_ref().display()),
                retry_after: max_retry_period,
            })
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
