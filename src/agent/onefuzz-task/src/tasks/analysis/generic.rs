// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::{
    config::CommonConfig, heartbeat::HeartbeatSender, report::crash_report::monitor_reports,
    utils::try_resolve_setup_relative_path,
};
use anyhow::{Context, Result};
use onefuzz::{az_copy, blob::url::BlobUrl};
use onefuzz::{
    expand::Expand,
    fs::{set_executable, OwnedDir},
    jitter::delay_with_jitter,
    process::monitor_process,
    syncdir::SyncedDir,
};
use serde::Deserialize;
use std::process::Stdio;
use std::{
    collections::HashMap,
    path::{Path, PathBuf},
    str,
};
use storage_queue::{QueueClient, EMPTY_QUEUE_DELAY};
use tempfile::tempdir_in;
use tokio::{fs, process::Command};

#[derive(Debug, Deserialize)]
pub struct Config {
    pub analyzer_exe: String,
    pub analyzer_options: Vec<String>,
    pub analyzer_env: HashMap<String, String>,

    pub target_exe: PathBuf,
    pub target_options: Vec<String>,
    pub input_queue: Option<QueueClient>,
    pub crashes: Option<SyncedDir>,

    pub analysis: SyncedDir,
    pub tools: Option<SyncedDir>,

    pub reports: Option<SyncedDir>,
    pub unique_reports: Option<SyncedDir>,
    pub no_repro: Option<SyncedDir>,

    #[serde(flatten)]
    pub common: CommonConfig,
}

impl Config {
    pub fn get_expand(&self) -> Expand<'_> {
        self.common
            .get_expand()
            .analyzer_exe(&self.analyzer_exe)
            .analyzer_options(&self.analyzer_options)
            .target_exe(&self.target_exe)
            .target_options(&self.target_options)
            .output_dir(&self.analysis.local_path)
            .set_optional(self.tools.clone().map(|t| t.local_path), Expand::tools_dir)
            .set_optional_ref(&self.reports, |expand, reports| {
                expand.reports_dir(reports.local_path.as_path())
            })
            .set_optional_ref(&self.crashes, |expand, crashes| {
                expand
                    .set_optional_ref(
                        &crashes.remote_path.clone().and_then(|u| u.account()),
                        |expand, account| expand.crashes_account(account),
                    )
                    .set_optional_ref(
                        &crashes.remote_path.clone().and_then(|u| u.container()),
                        |expand, container| expand.crashes_container(container),
                    )
            })
    }
}

pub async fn run(config: Config) -> Result<()> {
    let task_dir = config
        .analysis
        .local_path
        .parent()
        .ok_or_else(|| anyhow!("Invalid input path"))?;
    let temp_path = task_dir.join(".temp");
    tokio::fs::create_dir_all(&temp_path).await?;
    let tmp_dir = tempdir_in(&temp_path)?;
    let tmp = OwnedDir::new(tmp_dir.path());

    tmp.reset().await?;

    config.analysis.init().await?;
    if let Some(tools) = &config.tools {
        tools.init_pull().await?;
    }

    let job_result_client = config.common.init_job_result().await?;

    // the tempdir is always created, however, the reports_path and
    // reports_monitor_future are only created if we have one of the three
    // report SyncedDir. The idea is that the option for where to write reports
    // is only available for target option / env expansion if one of the reports
    // SyncedDir is provided.
    let reports_dir = tempdir_in(temp_path)?;
    let (reports_path, reports_monitor_future) =
        if config.unique_reports.is_some() || config.reports.is_some() || config.no_repro.is_some()
        {
            if let Some(unique_reports) = &config.unique_reports {
                unique_reports.init().await?;
            }
            if let Some(reports) = &config.reports {
                reports.init().await?;
            }
            if let Some(no_repro) = &config.no_repro {
                no_repro.init().await?;
            }
            let monitor_reports_future = monitor_reports(
                reports_dir.path(),
                &config.unique_reports,
                &config.reports,
                &config.no_repro,
                &job_result_client,
            );
            (
                Some(reports_dir.path().to_path_buf()),
                Some(monitor_reports_future),
            )
        } else {
            (None, None)
        };

    if let Some(tools) = &config.tools {
        set_executable(&tools.local_path).await?;
    }
    run_existing(&config, &reports_path).await?;
    let poller = poll_inputs(&config, tmp, &reports_path);

    match reports_monitor_future {
        Some(monitor) => {
            futures::try_join!(poller, monitor)?;
        }
        None => {
            poller.await?;
        }
    };

    Ok(())
}

async fn run_existing(config: &Config, reports_dir: &Option<PathBuf>) -> Result<()> {
    if let Some(crashes) = &config.crashes {
        info!("processing initial inputs");
        crashes.init_pull().await?;
        let mut count: u64 = 0;
        let mut read_dir = fs::read_dir(&crashes.local_path).await?;
        while let Some(file) = read_dir.next_entry().await? {
            debug!("Processing file {:?}", file);
            run_tool(file.path(), config, reports_dir).await?;
            count += 1;

            // sync the analysis container after every 10 inputs
            if count % 10 == 0 {
                config.analysis.sync_push().await?;
            }
        }
        info!("processed {} initial inputs", count);
        config.analysis.sync_push().await?;
    }
    Ok(())
}

async fn already_checked(config: &Config, input: &BlobUrl) -> Result<bool> {
    let result = if let Some(crashes) = &config.crashes {
        crashes.remote_path.clone().and_then(|u| u.account()) == input.account()
            && crashes.remote_path.clone().and_then(|u| u.container()) == input.container()
            && crashes.local_path.join(input.name()).exists()
    } else {
        false
    };

    Ok(result)
}

async fn poll_inputs(
    config: &Config,
    tmp_dir: OwnedDir,
    reports_dir: &Option<PathBuf>,
) -> Result<()> {
    info!("polling for new inputs");
    let heartbeat = config.common.init_heartbeat(None).await?;
    if let Some(input_queue) = &config.input_queue {
        loop {
            heartbeat.alive();
            if let Some(message) = input_queue.pop().await? {
                let input_url = message
                    .parse(|data| BlobUrl::parse(str::from_utf8(data)?))
                    .with_context(|| format!("unable to parse URL from queue: {message:?}"))?;
                if !already_checked(config, &input_url).await? {
                    let destination_path = _copy(input_url, &tmp_dir).await?;

                    run_tool(destination_path, config, reports_dir).await?;
                    config.analysis.sync_push().await?
                }
                message.delete().await?;
            } else {
                debug!("no new candidate inputs found, sleeping");
                delay_with_jitter(EMPTY_QUEUE_DELAY).await;
            }
        }
    }

    Ok(())
}

async fn _copy(input_url: BlobUrl, destination_folder: &OwnedDir) -> Result<PathBuf> {
    let file_name = input_url.name();
    let mut destination_path = PathBuf::from(destination_folder.path());
    destination_path.push(file_name);
    match input_url {
        BlobUrl::AzureBlob(input_url) => {
            az_copy::copy(input_url.as_ref(), &destination_path, false).await?
        }
        BlobUrl::LocalFile(path) => {
            tokio::fs::copy(path, &destination_path).await?;
        }
    }
    Ok(destination_path)
}

pub async fn run_tool(
    input: impl AsRef<Path>,
    config: &Config,
    reports_dir: &Option<PathBuf>,
) -> Result<()> {
    let target_exe =
        try_resolve_setup_relative_path(&config.common.setup_dir, &config.target_exe).await?;

    let expand = config
        .get_expand()
        .input_path(&input) // Only this one is dynamic, the other two should probably be a part of the config
        .target_exe(&target_exe)
        .set_optional_ref(reports_dir, Expand::reports_dir);

    let analyzer_path = expand.evaluate_value(&config.analyzer_exe)?;

    let mut cmd = Command::new(&analyzer_path);
    cmd.kill_on_drop(true)
        .env_remove("RUST_LOG")
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped());

    for arg in expand.evaluate(&config.analyzer_options)? {
        cmd.arg(arg);
    }

    for (k, v) in &config.analyzer_env {
        cmd.env(k, expand.evaluate_value(v)?);
    }

    info!("analyzing input with {:?}", cmd);
    let output = cmd
        .spawn()
        .with_context(|| format!("analyzer failed to start: {analyzer_path}"))?;

    monitor_process(output, "analyzer".to_string(), true, None)
        .await
        .with_context(|| format!("analyzer failed to run: {analyzer_path}"))?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use onefuzz::expand::PlaceHolder;
    use proptest::prelude::*;

    use crate::config_test_utils::GetExpandFields;

    use super::Config;

    impl GetExpandFields for Config {
        fn get_expand_fields(&self) -> Vec<(PlaceHolder, String)> {
            let mut params = self.common.get_expand_fields();
            params.push((
                PlaceHolder::AnalyzerExe,
                dunce::canonicalize(&self.analyzer_exe)
                    .unwrap()
                    .to_string_lossy()
                    .to_string(),
            ));
            params.push((
                PlaceHolder::AnalyzerOptions,
                self.analyzer_options.join(" "),
            ));
            params.push((
                PlaceHolder::TargetExe,
                dunce::canonicalize(&self.target_exe)
                    .unwrap()
                    .to_string_lossy()
                    .to_string(),
            ));
            params.push((PlaceHolder::TargetOptions, self.target_options.join(" ")));
            params.push((
                PlaceHolder::OutputDir,
                dunce::canonicalize(&self.analysis.local_path)
                    .unwrap()
                    .to_string_lossy()
                    .to_string(),
            ));
            if let Some(tools) = &self.tools {
                params.push((
                    PlaceHolder::ToolsDir,
                    dunce::canonicalize(&tools.local_path)
                        .unwrap()
                        .to_string_lossy()
                        .to_string(),
                ));
            }
            if let Some(reports) = &self.reports {
                params.push((
                    PlaceHolder::ReportsDir,
                    dunce::canonicalize(&reports.local_path)
                        .unwrap()
                        .to_string_lossy()
                        .to_string(),
                ));
            }
            if let Some(crashes) = &self.crashes {
                if let Some(account) = crashes.remote_path.clone().and_then(|u| u.account()) {
                    params.push((PlaceHolder::CrashesAccount, account));
                }
                if let Some(container) = crashes.remote_path.clone().and_then(|u| u.container()) {
                    params.push((PlaceHolder::CrashesContainer, container));
                }
            }

            params
        }
    }

    config_test!(Config);
}
