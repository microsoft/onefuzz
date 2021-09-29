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
use anyhow::Result;
use onefuzz::machine_id::{get_machine_id, get_scaleset_name};
use onefuzz_telemetry::{
    self as telemetry, Event::task_start, EventData, InstanceTelemetryKey, MicrosoftTelemetryKey,
    Role,
};
use reqwest::Url;
use serde::{self, Deserialize};
use std::{path::PathBuf, sync::Arc, time::Duration};
use uuid::Uuid;

#[derive(Debug, Deserialize, PartialEq, Clone)]
pub enum ContainerType {
    #[serde(alias = "inputs")]
    Inputs,
}

#[derive(Debug, Deserialize, Clone, Default)]
pub struct CommonConfig {
    pub job_id: Uuid,

    pub task_id: Uuid,

    pub instance_id: Uuid,

    pub heartbeat_queue: Option<Url>,

    pub instance_telemetry_key: Option<InstanceTelemetryKey>,

    pub microsoft_telemetry_key: Option<MicrosoftTelemetryKey>,

    #[serde(default)]
    pub setup_dir: PathBuf,
}

impl CommonConfig {
    pub async fn init_heartbeat(
        &self,
        initial_delay: Option<Duration>,
    ) -> Result<Option<TaskHeartbeatClient>> {
        match &self.heartbeat_queue {
            Some(url) => {
                let hb = init_task_heartbeat(url.clone(), self.task_id, self.job_id, initial_delay)
                    .await?;
                Ok(Some(hb))
            }
            None => Ok(None),
        }
    }
}

#[derive(Debug, Deserialize)]
#[serde(tag = "task_type")]
pub enum Config {
    #[cfg(any(target_os = "linux", target_os = "windows"))]
    #[serde(alias = "coverage")]
    Coverage(coverage::generic::Config),

    #[serde(alias = "libfuzzer_fuzz")]
    LibFuzzerFuzz(fuzz::libfuzzer_fuzz::Config),

    #[serde(alias = "libfuzzer_crash_report")]
    LibFuzzerReport(report::libfuzzer_report::Config),

    #[serde(alias = "libfuzzer_merge")]
    LibFuzzerMerge(merge::libfuzzer_merge::Config),

    #[cfg(any(target_os = "linux", target_os = "windows"))]
    #[serde(alias = "libfuzzer_coverage")]
    LibFuzzerCoverage(coverage::libfuzzer_coverage::Config),

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
    pub fn from_file(path: PathBuf, setup_dir: PathBuf) -> Result<Self> {
        let json = std::fs::read_to_string(path)?;
        let json_config: serde_json::Value = serde_json::from_str(&json)?;

        // override the setup_dir in the config file with the parameter value if specified
        let mut config: Self = serde_json::from_value(json_config)?;
        config.common_mut().setup_dir = setup_dir;

        Ok(config)
    }

    fn common_mut(&mut self) -> &mut CommonConfig {
        match self {
            #[cfg(any(target_os = "linux", target_os = "windows"))]
            Config::Coverage(c) => &mut c.common,
            Config::LibFuzzerFuzz(c) => &mut c.common,
            Config::LibFuzzerMerge(c) => &mut c.common,
            Config::LibFuzzerReport(c) => &mut c.common,
            #[cfg(any(target_os = "linux", target_os = "windows"))]
            Config::LibFuzzerCoverage(c) => &mut c.common,
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
            #[cfg(any(target_os = "linux", target_os = "windows"))]
            Config::Coverage(c) => &c.common,
            Config::LibFuzzerFuzz(c) => &c.common,
            Config::LibFuzzerMerge(c) => &c.common,
            Config::LibFuzzerReport(c) => &c.common,
            #[cfg(any(target_os = "linux", target_os = "windows"))]
            Config::LibFuzzerCoverage(c) => &c.common,
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
            #[cfg(any(target_os = "linux", target_os = "windows"))]
            Config::Coverage(_) => "coverage",
            Config::LibFuzzerFuzz(_) => "libfuzzer_fuzz",
            Config::LibFuzzerMerge(_) => "libfuzzer_merge",
            Config::LibFuzzerReport(_) => "libfuzzer_crash_report",
            #[cfg(any(target_os = "linux", target_os = "windows"))]
            Config::LibFuzzerCoverage(_) => "libfuzzer_coverage",
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
            }
            Config::GenericAnalysis(c) => {
                event!(task_start; EventData::Type = event_type, EventData::ToolName = c.analyzer_exe.clone());
            }
            _ => {
                event!(task_start; EventData::Type = event_type);
            }
        }
    }

    pub async fn run(self) -> Result<()> {
        telemetry::set_property(EventData::JobId(self.common().job_id));
        telemetry::set_property(EventData::TaskId(self.common().task_id));
        telemetry::set_property(EventData::MachineId(get_machine_id().await?));
        telemetry::set_property(EventData::Version(env!("ONEFUZZ_VERSION").to_string()));
        telemetry::set_property(EventData::InstanceId(self.common().instance_id));
        telemetry::set_property(EventData::Role(Role::Agent));
        let scaleset = get_scaleset_name().await?;
        if let Some(scaleset_name) = &scaleset {
            telemetry::set_property(EventData::ScalesetId(scaleset_name.to_string()));
        }

        info!("agent ready, dispatching task");
        self.report_event();

        match self {
            #[cfg(any(target_os = "linux", target_os = "windows"))]
            Config::Coverage(config) => coverage::generic::CoverageTask::new(config).run().await,
            Config::LibFuzzerFuzz(config) => {
                fuzz::libfuzzer_fuzz::LibFuzzerFuzzTask::new(config)?
                    .run()
                    .await
            }
            Config::LibFuzzerReport(config) => {
                report::libfuzzer_report::ReportTask::new(config)
                    .managed_run()
                    .await
            }
            #[cfg(any(target_os = "linux", target_os = "windows"))]
            Config::LibFuzzerCoverage(config) => {
                coverage::libfuzzer_coverage::CoverageTask::new(config)
                    .managed_run()
                    .await
            }
            Config::LibFuzzerMerge(config) => merge::libfuzzer_merge::spawn(Arc::new(config)).await,
            Config::GenericAnalysis(config) => analysis::generic::run(config).await,

            Config::GenericGenerator(config) => {
                fuzz::generator::GeneratorTask::new(config).run().await
            }
            Config::GenericSupervisor(config) => fuzz::supervisor::spawn(config).await,
            Config::GenericMerge(config) => merge::generic::spawn(Arc::new(config)).await,
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
        }
    }
}
