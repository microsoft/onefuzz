// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![allow(clippy::large_enum_variant)]
use crate::tasks::{analysis, coverage, fuzz, heartbeat::*, merge, report};
use anyhow::Result;
use onefuzz::{
    machine_id::{get_machine_id, get_scaleset_name},
    telemetry::{self, Event::task_start, EventData},
};
use reqwest::Url;
use serde::{self, Deserialize};
use std::path::{Path, PathBuf};
use std::sync::Arc;
use uuid::Uuid;

#[derive(Debug, Deserialize, PartialEq, Clone)]
pub enum ContainerType {
    #[serde(alias = "inputs")]
    Inputs,
}

#[derive(Debug, Deserialize, Clone)]
pub struct CommonConfig {
    pub job_id: Uuid,

    pub task_id: Uuid,

    pub instance_id: Uuid,

    pub instrumentation_key: Option<Uuid>,

    pub heartbeat_queue: Option<Url>,

    pub telemetry_key: Option<Uuid>,

    pub setup_dir: PathBuf,
}

impl CommonConfig {
    pub async fn init_heartbeat(&self) -> Result<Option<TaskHeartbeatClient>> {
        match &self.heartbeat_queue {
            Some(url) => {
                let hb = init_task_heartbeat(url.clone(), self.task_id).await?;
                Ok(Some(hb))
            }
            None => Ok(None),
        }
    }
}

#[derive(Debug, Deserialize)]
#[serde(tag = "task_type")]
pub enum Config {
    #[serde(alias = "libfuzzer_fuzz")]
    LibFuzzerFuzz(fuzz::libfuzzer_fuzz::Config),

    #[serde(alias = "libfuzzer_crash_report")]
    LibFuzzerReport(report::libfuzzer_report::Config),

    #[serde(alias = "libfuzzer_merge")]
    LibFuzzerMerge(merge::libfuzzer_merge::Config),

    #[serde(alias = "libfuzzer_coverage")]
    LibFuzzerCoverage(coverage::libfuzzer_coverage::Config),

    #[serde(alias = "generic_analysis")]
    GenericAnalysis(analysis::generic::Config),

    #[serde(alias = "generic_generator")]
    GenericGenerator(fuzz::generator::GeneratorConfig),

    #[serde(alias = "generic_supervisor")]
    GenericSupervisor(fuzz::supervisor::SupervisorConfig),

    #[serde(alias = "generic_merge")]
    GenericMerge(merge::generic::Config),

    #[serde(alias = "generic_crash_report")]
    GenericReport(report::generic::Config),
}

impl Config {
    pub fn from_file(path: impl AsRef<Path>, setup_dir: Option<impl AsRef<Path>>) -> Result<Self> {
        let json = std::fs::read_to_string(path)?;
        let mut json_config: serde_json::Value = serde_json::from_str(&json)?;
        if let Some(setup_dir) = setup_dir {
            json_config["setup_dir"] =
                serde_json::Value::String(setup_dir.as_ref().to_string_lossy().into());
        }

        Ok(serde_json::from_value(json_config)?)
    }

    pub fn common(&self) -> &CommonConfig {
        match self {
            Config::LibFuzzerFuzz(c) => &c.common,
            Config::LibFuzzerMerge(c) => &c.common,
            Config::LibFuzzerReport(c) => &c.common,
            Config::LibFuzzerCoverage(c) => &c.common,
            Config::GenericAnalysis(c) => &c.common,
            Config::GenericMerge(c) => &c.common,
            Config::GenericReport(c) => &c.common,
            Config::GenericSupervisor(c) => &c.common,
            Config::GenericGenerator(c) => &c.common,
        }
    }

    pub fn report_event(&self) {
        let event_type = match self {
            Config::LibFuzzerFuzz(_) => "libfuzzer_fuzz",
            Config::LibFuzzerMerge(_) => "libfuzzer_merge",
            Config::LibFuzzerReport(_) => "libfuzzer_crash_report",
            Config::LibFuzzerCoverage(_) => "libfuzzer_coverage",
            Config::GenericAnalysis(_) => "generic_analysis",
            Config::GenericMerge(_) => "generic_merge",
            Config::GenericReport(_) => "generic_crash_report",
            Config::GenericSupervisor(_) => "generic_supervisor",
            Config::GenericGenerator(_) => "generic_generator",
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
        let scaleset = get_scaleset_name().await?;
        if let Some(scaleset_name) = &scaleset {
            telemetry::set_property(EventData::ScalesetId(scaleset_name.to_string()));
        }

        info!("agent ready, dispatching task");
        self.report_event();

        match self {
            Config::LibFuzzerFuzz(config) => {
                fuzz::libfuzzer_fuzz::LibFuzzerFuzzTask::new(config)?
                    .start()
                    .await
            }
            Config::LibFuzzerReport(config) => {
                report::libfuzzer_report::ReportTask::new(config)
                    .run()
                    .await
            }
            Config::LibFuzzerCoverage(config) => {
                coverage::libfuzzer_coverage::CoverageTask::new(Arc::new(config))
                    .run()
                    .await
            }
            Config::LibFuzzerMerge(config) => merge::libfuzzer_merge::spawn(Arc::new(config)).await,
            Config::GenericAnalysis(config) => analysis::generic::spawn(config).await,
            Config::GenericGenerator(config) => fuzz::generator::spawn(Arc::new(config)).await,
            Config::GenericSupervisor(config) => fuzz::supervisor::spawn(config).await,
            Config::GenericMerge(config) => merge::generic::spawn(Arc::new(config)).await,
            Config::GenericReport(config) => report::generic::ReportTask::new(&config).run().await,
        }
    }
}
