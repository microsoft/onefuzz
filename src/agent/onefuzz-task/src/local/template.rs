use async_trait::async_trait;
use flume::Sender;
use onefuzz::{blob::BlobContainerUrl, syncdir::SyncedDir, utils::try_wait_all_join_handles};
use path_absolutize::Absolutize;
use serde::Deserialize;
use std::path::{Path, PathBuf};
use storage_queue::QueueClient;
use strum_macros::{EnumDiscriminants, EnumString, EnumVariantNames};
use tokio::{sync::Mutex, task::JoinHandle};
use url::Url;
use uuid::Uuid;

use crate::local::{
    coverage::Coverage, generic_analysis::Analysis, generic_crash_report::CrashReport,
    generic_generator::Generator, libfuzzer::LibFuzzer,
    libfuzzer_crash_report::LibfuzzerCrashReport, libfuzzer_merge::LibfuzzerMerge,
    libfuzzer_regression::LibfuzzerRegression, libfuzzer_test_input::LibfuzzerTestInput,
    test_input::TestInput,
};
use crate::tasks::config::CommonConfig;

use super::common::{DirectoryMonitorQueue, SyncCountDirMonitor, UiEvent};
use anyhow::{Error, Result};

use schemars::JsonSchema;

/// A group of task to run
#[derive(Debug, Serialize, Deserialize, Clone, JsonSchema)]
pub struct TaskGroup {
    #[serde(flatten)]
    pub common: CommonProperties,
    /// The list of tasks
    pub tasks: Vec<TaskConfig>,
}

#[derive(Debug, Deserialize, Serialize, Clone, JsonSchema)]

pub struct CommonProperties {
    pub setup_dir: Option<PathBuf>,
    pub extra_setup_dir: Option<PathBuf>,
    pub extra_dir: Option<PathBuf>,
    #[serde(default)]
    pub create_job_dir: bool,
}

#[derive(Debug, Serialize, Deserialize, Clone, JsonSchema, EnumVariantNames, EnumDiscriminants)]
#[strum_discriminants(derive(EnumString))]
#[serde(tag = "type")]
pub enum TaskConfig {
    LibFuzzer(LibFuzzer),
    Analysis(Analysis),
    Coverage(Coverage),
    CrashReport(CrashReport),
    Generator(Generator),
    LibfuzzerCrashReport(LibfuzzerCrashReport),
    LibfuzzerMerge(LibfuzzerMerge),
    LibfuzzerRegression(LibfuzzerRegression),
    LibfuzzerTestInput(LibfuzzerTestInput),
    TestInput(TestInput),
    /// The radamsa task can be represented via a combination of the `Generator` and `Report` tasks.
    /// Please see `src/agent/onefuzz-task/src/local/example_templates/radamsa.yml` for an example template
    Radamsa,
}

#[async_trait]
pub trait Template<T> {
    fn example_values() -> T;
    async fn run(&self, context: &RunContext) -> Result<()>;
}

impl TaskConfig {
    async fn launch(&self, context: RunContext) -> Result<RunContext> {
        match self {
            TaskConfig::LibFuzzer(config) => {
                config.run(&context).await?;
            }
            TaskConfig::Analysis(config) => {
                config.run(&context).await?;
            }
            TaskConfig::Coverage(config) => {
                config.run(&context).await?;
            }
            TaskConfig::CrashReport(config) => {
                config.run(&context).await?;
            }
            TaskConfig::Generator(config) => {
                config.run(&context).await?;
            }
            TaskConfig::LibfuzzerCrashReport(config) => {
                config.run(&context).await?;
            }
            TaskConfig::LibfuzzerMerge(config) => {
                config.run(&context).await?;
            }
            TaskConfig::LibfuzzerRegression(config) => {
                config.run(&context).await?;
            }
            TaskConfig::LibfuzzerTestInput(config) => {
                config.run(&context).await?;
            }
            TaskConfig::TestInput(config) => {
                config.run(&context).await?;
            }
            TaskConfig::Radamsa => {}
        }

        Ok(context)
    }
}

pub struct RunContext {
    monitor_queues: Mutex<Vec<DirectoryMonitorQueue>>,
    tasks_handle: Mutex<Vec<JoinHandle<Result<()>>>>,
    pub common: CommonConfig,
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

    pub async fn monitor_dir(&self, watch: impl AsRef<Path>) -> Result<QueueClient> {
        let monitor_q = DirectoryMonitorQueue::start_monitoring(watch).await?;
        let q_client = monitor_q.queue_client.clone();
        self.monitor_queues.lock().await.push(monitor_q);
        Ok(q_client)
    }

    pub fn to_monitored_sync_dir(
        &self,
        name: impl AsRef<str>,
        path: impl AsRef<Path>,
    ) -> Result<SyncedDir> {
        self.to_sync_dir(name, path)?
            .monitor_count(&self.event_sender)
    }

    pub fn to_sync_dir(&self, name: impl AsRef<str>, path: impl AsRef<Path>) -> Result<SyncedDir> {
        let path = path.as_ref();
        if !path.exists() {
            std::fs::create_dir_all(path)?;
        }

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
        self.add_handle(handle).await;
    }

    pub async fn add_handle(&self, handle: JoinHandle<Result<(), Error>>) {
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
        job_id: Uuid::new_v4(),
        instance_id: Uuid::new_v4(),
        heartbeat_queue: None,
        job_result_queue: None,
        instance_telemetry_key: None,
        microsoft_telemetry_key: None,
        logs: None,
        setup_dir: task_group.common.setup_dir.unwrap_or_default(),
        extra_setup_dir: task_group.common.extra_setup_dir,
        min_available_memory_mb: crate::tasks::config::default_min_available_memory_mb(),
        machine_identity: onefuzz::machine_id::MachineIdentity {
            machine_id: Uuid::nil(),
            machine_name: "local".to_string(),
            scaleset_name: None,
        },
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
    #[test]
    fn test() {
        let schema = schemars::schema_for!(super::TaskGroup);
        let schema_str = serde_json::to_string_pretty(&schema)
            .unwrap()
            .replace("\r\n", "\n");

        let checked_in_schema = std::fs::read_to_string("src/local/schema.json")
            .expect("Couldn't find checked-in schema.json")
            .replace("\r\n", "\n");

        if schema_str.replace('\n', "") != checked_in_schema.replace('\n', "") {
            std::fs::write("src/local/new.schema.json", schema_str)
                .expect("The schemas did not match but failed to write new schema to file.");
            panic!("The checked-in local fuzzing schema did not match the generated schema. The generated schema can be found at src/local/new.schema.json");
        }
    }
}
