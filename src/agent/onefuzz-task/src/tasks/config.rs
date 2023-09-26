// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![allow(clippy::large_enum_variant)]
#[cfg(any(target_os = "linux", target_os = "windows"))]
use crate::tasks::coverage;
use crate::tasks::{
    analysis, fuzz,
    heartbeat::{init_task_heartbeat, TaskHeartbeatClient},
    merge, regression, report,
};
use anyhow::{Context, Result};
use onefuzz::{
    machine_id::MachineIdentity,
    syncdir::{SyncOperation, SyncedDir}, expand::{ToExpand, Expand},
};
use onefuzz_result::job_result::{init_job_result, TaskJobResultClient};
use onefuzz_telemetry::{
    self as telemetry, Event::task_start, EventData, InstanceTelemetryKey, MicrosoftTelemetryKey,
    Role,
};
use reqwest::Url;
use serde::{self, Deserialize};
use std::{
    collections::HashMap,
    path::{Path, PathBuf},
    time::Duration,
};
use tokio_util::sync::CancellationToken;
use uuid::Uuid;

const DEFAULT_MIN_AVAILABLE_MEMORY_MB: u64 = 100;

pub fn default_min_available_memory_mb() -> u64 {
    DEFAULT_MIN_AVAILABLE_MEMORY_MB
}

#[derive(Debug, Deserialize, PartialEq, Eq, Clone)]
pub enum ContainerType {
    #[serde(alias = "inputs")]
    Inputs,
}

#[derive(Debug, Deserialize, Clone)]
pub struct CommonConfig {
    pub job_id: Uuid,

    pub task_id: Uuid,

    pub instance_id: Uuid,

    pub heartbeat_queue: Option<Url>,

    pub job_result_queue: Option<Url>,

    pub instance_telemetry_key: Option<InstanceTelemetryKey>,

    pub microsoft_telemetry_key: Option<MicrosoftTelemetryKey>,

    pub logs: Option<Url>,

    #[serde(default)]
    pub setup_dir: PathBuf,

    #[serde(default)]
    pub extra_setup_dir: Option<PathBuf>,

    #[serde(default)]
    pub extra_output: Option<SyncedDir>,

    /// Lower bound on available system memory. If the available memory drops
    /// below the limit, the task will exit with an error. This is a fail-fast
    /// mechanism to support debugging.
    ///
    /// Can be disabled by setting to 0.
    #[serde(default = "default_min_available_memory_mb")]
    pub min_available_memory_mb: u64,

    pub machine_identity: MachineIdentity,

    #[serde(default)]
    pub tags: HashMap<String, String>,

    pub from_agent_to_task_endpoint: String,
    pub from_task_to_agent_endpoint: String,
}

impl CommonConfig {
    pub async fn init_heartbeat(
        &self,
        initial_delay: Option<Duration>,
    ) -> Result<Option<TaskHeartbeatClient>> {
        match &self.heartbeat_queue {
            Some(url) => {
                let hb = init_task_heartbeat(
                    url.clone(),
                    self.task_id,
                    self.job_id,
                    initial_delay,
                    self.machine_identity.machine_id,
                    self.machine_identity.machine_name.clone(),
                )
                .await?;
                Ok(Some(hb))
            }
            None => Ok(None),
        }
    }

    pub async fn init_job_result(&self) -> Result<Option<TaskJobResultClient>> {
        match &self.job_result_queue {
            Some(url) => {
                let result = init_job_result(
                    url.clone(),
                    self.task_id,
                    self.job_id,
                    self.machine_identity.machine_id,
                    self.machine_identity.machine_name.clone(),
                )
                .await?;
                Ok(Some(result))
            }
            None => Ok(None),
        }
    }
}

impl ToExpand for CommonConfig {
    fn to_expand<'a>(&'a self) -> Expand<'a> {
        Expand::new(&self.machine_identity)
        .machine_id()
        .job_id(&self.job_id)
        .task_id(&self.task_id)
        .set_optional_ref(&self.instance_telemetry_key, Expand::instance_telemetry_key)
        .set_optional_ref(&self.microsoft_telemetry_key, Expand::microsoft_telemetry_key)
        .setup_dir(&self.setup_dir)
        .set_optional_ref(&self.extra_setup_dir, Expand::extra_setup_dir)
        .set_optional_ref(&self.extra_output, |expand, value| {
            expand.extra_output_dir(value.local_path.as_path())
        })
    }
}

#[derive(Debug, Deserialize)]
#[serde(tag = "task_type")]
pub enum Config {
    #[serde(alias = "coverage")]
    Coverage(coverage::generic::Config),

    #[serde(alias = "dotnet_coverage")]
    DotnetCoverage(coverage::dotnet::Config),

    #[serde(alias = "dotnet_crash_report")]
    DotnetCrashReport(report::dotnet::generic::Config),

    #[serde(alias = "libfuzzer_dotnet_fuzz")]
    LibFuzzerDotnetFuzz(fuzz::libfuzzer::dotnet::Config),

    #[serde(alias = "libfuzzer_fuzz")]
    LibFuzzerFuzz(fuzz::libfuzzer::generic::Config),

    #[serde(alias = "libfuzzer_crash_report")]
    LibFuzzerReport(report::libfuzzer_report::Config),

    #[serde(alias = "libfuzzer_merge")]
    LibFuzzerMerge(merge::libfuzzer_merge::Config),

    #[serde(alias = "libfuzzer_regression")]
    LibFuzzerRegression(regression::libfuzzer::Config),

    #[serde(alias = "generic_analysis")]
    GenericAnalysis(analysis::generic::Config),

    #[serde(alias = "generic_generator")]
    GenericGenerator(fuzz::generator::Config),

    #[serde(alias = "generic_supervisor")]
    GenericSupervisor(fuzz::supervisor::SupervisorConfig),

    #[serde(alias = "generic_merge")]
    GenericMerge(merge::generic::Config),

    #[serde(alias = "generic_crash_report")]
    GenericReport(report::generic::Config),

    #[serde(alias = "generic_regression")]
    GenericRegression(regression::generic::Config),
}

impl Config {
    pub fn from_file(
        path: &Path,
        setup_dir: PathBuf,
        extra_setup_dir: Option<PathBuf>,
    ) -> Result<Self> {
        let json = std::fs::read_to_string(path)
            .with_context(|| format!("loading config from {}", path.display()))?;

        let mut config = serde_json::from_str::<Self>(&json).context("deserializing Config")?;

        // override the setup_dir in the config file with the parameter value if specified
        config.common_mut().setup_dir = setup_dir;
        config.common_mut().extra_setup_dir = extra_setup_dir;

        Ok(config)
    }

    fn common_mut(&mut self) -> &mut CommonConfig {
        match self {
            Config::Coverage(c) => &mut c.common,
            Config::DotnetCoverage(c) => &mut c.common,
            Config::DotnetCrashReport(c) => &mut c.common,
            Config::LibFuzzerDotnetFuzz(c) => &mut c.common,
            Config::LibFuzzerFuzz(c) => &mut c.common,
            Config::LibFuzzerMerge(c) => &mut c.common,
            Config::LibFuzzerReport(c) => &mut c.common,
            Config::LibFuzzerRegression(c) => &mut c.common,
            Config::GenericAnalysis(c) => &mut c.common,
            Config::GenericMerge(c) => &mut c.common,
            Config::GenericReport(c) => &mut c.common,
            Config::GenericSupervisor(c) => &mut c.common,
            Config::GenericGenerator(c) => &mut c.common,
            Config::GenericRegression(c) => &mut c.common,
        }
    }

    pub fn common(&self) -> &CommonConfig {
        match self {
            Config::Coverage(c) => &c.common,
            Config::DotnetCoverage(c) => &c.common,
            Config::DotnetCrashReport(c) => &c.common,
            Config::LibFuzzerDotnetFuzz(c) => &c.common,
            Config::LibFuzzerFuzz(c) => &c.common,
            Config::LibFuzzerMerge(c) => &c.common,
            Config::LibFuzzerReport(c) => &c.common,
            Config::LibFuzzerRegression(c) => &c.common,
            Config::GenericAnalysis(c) => &c.common,
            Config::GenericMerge(c) => &c.common,
            Config::GenericReport(c) => &c.common,
            Config::GenericSupervisor(c) => &c.common,
            Config::GenericGenerator(c) => &c.common,
            Config::GenericRegression(c) => &c.common,
        }
    }

    pub fn report_event(&self) {
        let event_type = match self {
            Config::Coverage(_) => "coverage",
            Config::DotnetCoverage(_) => "dotnet_coverage",
            Config::DotnetCrashReport(_) => "dotnet_crash_report",
            Config::LibFuzzerDotnetFuzz(_) => "libfuzzer_fuzz",
            Config::LibFuzzerFuzz(_) => "libfuzzer_fuzz",
            Config::LibFuzzerMerge(_) => "libfuzzer_merge",
            Config::LibFuzzerReport(_) => "libfuzzer_crash_report",
            Config::LibFuzzerRegression(_) => "libfuzzer_regression",
            Config::GenericAnalysis(_) => "generic_analysis",
            Config::GenericMerge(_) => "generic_merge",
            Config::GenericReport(_) => "generic_crash_report",
            Config::GenericSupervisor(_) => "generic_supervisor",
            Config::GenericGenerator(_) => "generic_generator",
            Config::GenericRegression(_) => "generic_regression",
        };

        match self {
            Config::GenericGenerator(c) => {
                event!(task_start; EventData::Type = event_type, EventData::ToolName = c.generator_exe.clone());
                metric!(task_start; 1.0; EventData::Type = event_type, EventData::ToolName = c.generator_exe.clone());
            }
            Config::GenericAnalysis(c) => {
                event!(task_start; EventData::Type = event_type, EventData::ToolName = c.analyzer_exe.clone());
                metric!(task_start; 1.0; EventData::Type = event_type, EventData::ToolName = c.analyzer_exe.clone());
            }
            _ => {
                event!(task_start; EventData::Type = event_type);
                metric!(task_start; 1.0; EventData::Type = event_type);
            }
        }
    }

    pub async fn run(self) -> Result<()> {
        telemetry::set_property(EventData::JobId(self.common().job_id));
        telemetry::set_property(EventData::TaskId(self.common().task_id));
        telemetry::set_property(EventData::MachineId(
            self.common().machine_identity.machine_id,
        ));
        telemetry::set_property(EventData::Version(env!("ONEFUZZ_VERSION").to_string()));
        telemetry::set_property(EventData::InstanceId(self.common().instance_id));
        telemetry::set_property(EventData::Role(Role::Agent));

        if let Some(scaleset_name) = &self.common().machine_identity.scaleset_name {
            telemetry::set_property(EventData::ScalesetId(scaleset_name.to_string()));
        }

        info!("agent ready, dispatching task");
        self.report_event();

        let extra_output_dir = self.common().extra_output.clone();
        if let Some(dir) = &extra_output_dir {
            // setup the directory
            dir.init().await.context("initing extra_output_dir")?;
        }

        let sync_cancellation = CancellationToken::new();
        let background_sync_task = async {
            if let Some(dir) = extra_output_dir {
                // push it continually
                dir.continuous_sync(SyncOperation::Push, None, &sync_cancellation)
                    .await?;

                // when we are cancelled, do one more sync, to ensure
                // everything is up-to-date
                dir.sync_push().await?;

                Ok(())
            } else {
                Ok(())
            }
        };

        let run_task = async {
            let result = match self {
                Config::Coverage(config) => {
                    coverage::generic::CoverageTask::new(config).run().await
                }
                Config::DotnetCoverage(config) => {
                    coverage::dotnet::DotnetCoverageTask::new(config)
                        .run()
                        .await
                }
                Config::DotnetCrashReport(config) => {
                    report::dotnet::generic::DotnetCrashReportTask::new(config)
                        .run()
                        .await
                }
                Config::LibFuzzerDotnetFuzz(config) => {
                    fuzz::libfuzzer::dotnet::LibFuzzerDotnetFuzzTask::new(config)?
                        .run()
                        .await
                }
                Config::LibFuzzerFuzz(config) => {
                    fuzz::libfuzzer::generic::LibFuzzerFuzzTask::new(config)?
                        .run()
                        .await
                }
                Config::LibFuzzerReport(config) => {
                    report::libfuzzer_report::ReportTask::new(config)
                        .managed_run()
                        .await
                }
                Config::LibFuzzerMerge(config) => merge::libfuzzer_merge::spawn(config).await,
                Config::GenericAnalysis(config) => analysis::generic::run(config).await,
                Config::GenericGenerator(config) => {
                    fuzz::generator::GeneratorTask::new(config).run().await
                }
                Config::GenericSupervisor(config) => fuzz::supervisor::spawn(config).await,
                Config::GenericMerge(config) => merge::generic::spawn(&config).await,
                Config::GenericReport(config) => {
                    report::generic::ReportTask::new(config).managed_run().await
                }
                Config::GenericRegression(config) => {
                    regression::generic::GenericRegressionTask::new(config)
                        .run()
                        .await
                }
                Config::LibFuzzerRegression(config) => {
                    regression::libfuzzer::LibFuzzerRegressionTask::new(config)
                        .run()
                        .await
                }
            };

            // once main task is complete, cancel sync;
            // this will stop continuous sync and perform one final sync
            sync_cancellation.cancel();

            result
        };

        tokio::try_join!(run_task, background_sync_task)?;
        Ok(())
    }
}
