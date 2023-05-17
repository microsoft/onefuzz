use flume::Sender;
use onefuzz::{blob::BlobContainerUrl, syncdir::SyncedDir, utils::try_wait_all_join_handles};
use path_absolutize::Absolutize;
use std::{
    collections::HashMap,
    path::{Path, PathBuf},
};
use url::Url;
use uuid::Uuid;
// use clap::{Parser, Subcommand};
use serde::Deserialize;
use storage_queue::QueueClient;
use tokio::{sync::Mutex, task::JoinHandle};

use crate::tasks::{
    config::CommonConfig,
    fuzz::{
        self,
        libfuzzer::{
            common::default_workers,
            generic::LibFuzzerFuzzTask,
        },
    },
    report,
};

use super::common::{DirectoryMonitorQueue, SyncCountDirMonitor, UiEvent};
use anyhow::Result;

use futures::{future::OptionFuture};

use schemars::{schema_for, JsonSchema};

/// A group of task to run
#[derive(Debug, Serialize, Deserialize, Clone, JsonSchema)]
pub struct TaskGroup {
    #[serde(flatten)]
    common: CommonProperties,
    /// The list of tasks
    tasks: Vec<TaskConfig>,
}

#[derive(Debug, Deserialize, Serialize, Clone, JsonSchema)]

struct CommonProperties {
    pub job_id: Option<Uuid>,
    pub target_exe: Option<PathBuf>,
    pub target_options: Vec<String>,
    pub target_env: Vec<(String, String)>,
    pub setup_dir: Option<PathBuf>,
    pub extra_setup_dir: Option<PathBuf>,
    pub extra_dir: Option<PathBuf>,
    #[serde(default)]
    pub create_job_dir: bool,
}

#[derive(Debug, Serialize, Clone, JsonSchema, Deserialize)]
#[serde(from = "String")]
struct FolderWatch {
    /// The path to watch
    path: PathBuf,
}

impl From<String> for FolderWatch {
    fn from(path: String) -> Self {
        Self { path: PathBuf::from(path) }
    }
}


#[derive(Debug, Serialize, Deserialize, Clone, JsonSchema)]
struct LibFuzzer {
    inputs: PathBuf,
    readonly_inputs: Vec<PathBuf>,
    crashes: PathBuf,
    target_exe: PathBuf,
    target_env: HashMap<String, String>,
    target_options: Vec<String>,
    target_workers: Option<usize>,
    ensemble_sync_delay: Option<u64>,
    #[serde(default = "default_bool_true")]
    check_fuzzer_help: bool,
    #[serde(default)]
    expect_crash_on_failure: bool,
}

#[derive(Debug, Serialize, Deserialize, Clone, JsonSchema)]
struct Analysis {
    analyzer_exe: String,
    analyzer_options: Vec<String>,
    analyzer_env: HashMap<String, String>,
    target_exe: PathBuf,
    target_options: Vec<String>,
    input_queue: Option<FolderWatch>,
    crashes: Option<PathBuf>,
    analysis: PathBuf,
    tools: PathBuf,
    reports: Option<PathBuf>,
    unique_reports: Option<PathBuf>,
    no_repro: Option<PathBuf>,
}

#[derive(Debug, Serialize, Deserialize, Clone, JsonSchema)]
struct Report {
    target_exe: PathBuf,
    target_env: HashMap<String, String>,
    // TODO:  options are not yet used for crash reporting
    target_options: Vec<String>,
    target_timeout: Option<u64>,
    input_queue: Option<FolderWatch>,
    crashes: Option<PathBuf>,
    reports: Option<PathBuf>,
    unique_reports: Option<PathBuf>,
    no_repro: Option<PathBuf>,
    #[serde(default = "default_bool_true")]
    check_fuzzer_help: bool,
    #[serde(default)]
    check_retry_count: u64,
    #[serde(default)]
    minimized_stack_depth: Option<usize>,
    #[serde(default = "default_bool_true")]
    check_queue: bool,
}

pub fn default_bool_true() -> bool {
    true
}

#[derive(Debug, Serialize, Deserialize, Clone, JsonSchema)]
struct Coverage {
    target_exe: PathBuf,
    target_env: HashMap<String, String>,
    target_options: Vec<String>,
    target_timeout: Option<u64>,
    module_allowlist: Option<String>,
    source_allowlist: Option<String>,
    input_queue: Option<FolderWatch>,
    readonly_inputs: Vec<PathBuf>,
    coverage: PathBuf,
}

#[derive(Debug, Serialize, Deserialize, Clone, JsonSchema)]
#[serde(tag = "type")]
enum TaskConfig {
    LibFuzzer(LibFuzzer),
    Analysis(Analysis),
    Coverage(Coverage),
    Report(Report),
}

impl TaskConfig {
    async fn launch(&self, context: RunContext) -> Result<RunContext> {
        match self {
            TaskConfig::LibFuzzer(config) => {
                let ri: Result<Vec<SyncedDir>> = config
                    .readonly_inputs
                    .iter()
                    .enumerate()
                    .map(|(index, input)| {
                        context.to_sync_dir(format!("readonly_inputs_{index}"), input)
                    })
                    .collect();

                let libfuzzer_config = fuzz::libfuzzer::generic::Config {
                    inputs: context.to_monitored_sync_dir("inputs", &config.inputs)?,
                    readonly_inputs: Some(ri?),
                    crashes: context.to_monitored_sync_dir("crashes", &config.crashes)?,
                    target_exe: config.target_exe.clone(),
                    target_env: config.target_env.clone(),
                    target_options: config.target_options.clone(),
                    target_workers: config.target_workers.unwrap_or(default_workers()),
                    ensemble_sync_delay: config.ensemble_sync_delay,
                    check_fuzzer_help: config.check_fuzzer_help,
                    expect_crash_on_failure: config.expect_crash_on_failure,
                    extra: (),
                    common: CommonConfig {
                        task_id: uuid::Uuid::new_v4(),
                        ..context.common.clone()
                    },
                };

                context
                    .spawn(async move {
                        let fuzzer = LibFuzzerFuzzTask::new(libfuzzer_config)?;
                        fuzzer.run().await
                    })
                    .await;
            }
            TaskConfig::Analysis(_analysis) => {}
            TaskConfig::Coverage(_) => todo!(),
            TaskConfig::Report(config) => {
                let input_q_fut: OptionFuture<_> = config
                    .input_queue
                    .iter()
                    .map(|w| context.monitor_dir(w))
                    .next()
                    .into();

                let input_q = input_q_fut.await.transpose()?;
                let report_config = report::libfuzzer_report::Config {
                    target_exe: config.target_exe.clone(),
                    target_env: config.target_env.clone(),
                    target_options: config.target_options.clone(),
                    target_timeout: config.target_timeout,
                    input_queue: input_q,
                    crashes: config
                        .crashes
                        .clone()
                        .map(|c| context.to_monitored_sync_dir("crashes", c))
                        .transpose()?,
                    reports: config
                        .reports
                        .clone()
                        .map(|c| context.to_monitored_sync_dir("reports", c))
                        .transpose()?,
                    unique_reports: config
                        .unique_reports
                        .clone()
                        .map(|c| context.to_monitored_sync_dir("unique_reports", c))
                        .transpose()?,
                    no_repro: config
                        .no_repro
                        .clone()
                        .map(|c| context.to_monitored_sync_dir("no_repro", c))
                        .transpose()?,
                    check_fuzzer_help: config.check_fuzzer_help,
                    check_retry_count: config.check_retry_count,
                    minimized_stack_depth: config.minimized_stack_depth,
                    check_queue: config.check_queue,
                    common: CommonConfig {
                        task_id: uuid::Uuid::new_v4(),
                        ..context.common.clone()
                    },
                };

                context
                    .spawn(async move {
                        let mut report = report::libfuzzer_report::ReportTask::new(report_config);
                        report.managed_run().await
                    })
                    .await;
            }
        }

        Ok(context)
    }
}

struct RunContext {
    monitor_queues: Mutex<Vec<DirectoryMonitorQueue>>,
    tasks_handle: Mutex<Vec<JoinHandle<Result<()>>>>,
    common: CommonConfig,
    event_sender: Option<Sender<UiEvent>>,
    create_job_dir: bool,
}

impl RunContext {
    fn new(common: CommonConfig, event_sender: Option<Sender<UiEvent>>) -> Self {
        Self {
            monitor_queues: Mutex::new(Vec::new()),
            common,
            event_sender,
            tasks_handle: Mutex::new(Vec::new()),
            create_job_dir: false,
        }
    }

    async fn monitor_dir(&self, watch: &FolderWatch) -> Result<QueueClient> {
        let monitor_q = DirectoryMonitorQueue::start_monitoring(watch.path.clone()).await?;
        let q_client = monitor_q.queue_client.clone();
        self.monitor_queues.lock().await.push(monitor_q);
        Ok(q_client)
    }

    // trait ToSyncDir {
    //     fn to_sync_dir(&self, create_job_dir: bool ) -> Result<SyncedDir>;
    fn to_monitored_sync_dir(
        &self,
        name: impl AsRef<str>,
        path: impl AsRef<Path>,
    ) -> Result<SyncedDir> {
        self.to_sync_dir(name, path)?
            .monitor_count(&self.event_sender)
    }
    // }

    fn to_sync_dir(&self, name: impl AsRef<str>, path: impl AsRef<Path>) -> Result<SyncedDir> {
        let path = path.as_ref();
        let name = name.as_ref();
        let current_dir = std::env::current_dir()?;
        if self.create_job_dir {
            let remote_path = path.absolutize()?;
            let remote_url = Url::from_file_path(remote_path).expect("invalid file path");
            let remote_blob_url = BlobContainerUrl::new(remote_url).expect("invalid url");
            let job_id = self.common.job_id;
            let task_id = self.common.task_id;
            let path = current_dir.join(format!("{job_id}/{task_id}/{name}"));
            Ok(SyncedDir {
                remote_path: Some(remote_blob_url),
                local_path: path,
            })
        } else {
            Ok(SyncedDir {
                remote_path: None,
                local_path: PathBuf::from(path),
            })
        }
    }

    pub async fn spawn(
        &self,
        future: impl futures::Future<Output = Result<()>> + std::marker::Send + 'static,
    ) {
        let handle = tokio::spawn(future);
        self.tasks_handle.lock().await.push(handle);
    }
}

pub async fn launch(
    task_group_config: impl AsRef<Path>,
    event_sender: Option<Sender<UiEvent>>,
) -> Result<()> {
    let file = std::fs::File::open(task_group_config.as_ref())?;
    let reader = std::io::BufReader::new(file);
    let mut value: serde_yaml::Value = serde_yaml::from_reader(reader)?;
    value.apply_merge()?;

    let task_group: TaskGroup = serde_yaml::from_value(value)?;
    let common = CommonConfig {
        task_id: Uuid::nil(),
        job_id: task_group.common.job_id.unwrap_or(Uuid::new_v4()),
        instance_id: Uuid::new_v4(),
        heartbeat_queue: None,
        instance_telemetry_key: None,
        microsoft_telemetry_key: None,
        logs: None,
        setup_dir: task_group.common.setup_dir.unwrap_or_default(),
        extra_setup_dir: task_group.common.extra_setup_dir,
        min_available_memory_mb: crate::tasks::config::default_min_available_memory_mb(),
        machine_identity: onefuzz::machine_id::MachineIdentity::from_metadata().await?,
        tags: Default::default(),
        from_agent_to_task_endpoint: "/".to_string(),
        from_task_to_agent_endpoint: "/".to_string(),
        extra_output: None,
    };

    let mut context = RunContext::new(common, event_sender);

    for task in task_group.tasks {
        context = task.launch(context).await?;
    }
    let handles = context
        .tasks_handle
        .lock()
        .await
        .drain(..)
        .collect::<Vec<_>>();
    try_wait_all_join_handles(handles).await?;

    Ok(())
}



mod test {
    use schemars;
    #[test]
    fn test() {
        let schema = schemars::schema_for!(super::TaskGroup);
        println!("{}", serde_json::to_string_pretty(&schema).unwrap());

    }

}
