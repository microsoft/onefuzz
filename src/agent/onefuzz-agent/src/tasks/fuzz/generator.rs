// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::{
    config::{CommonConfig, SyncedDir},
    heartbeat::*,
    utils,
};
use anyhow::{Error, Result};
use futures::stream::StreamExt;
use onefuzz::{expand::Expand, fs::set_executable, input_tester::Tester, sha256};
use serde::Deserialize;
use std::collections::HashMap;
use std::{
    ffi::OsString,
    path::{Path, PathBuf},
    process::Stdio,
    sync::Arc,
};
use tokio::{fs, process::Command};

fn default_bool_true() -> bool {
    true
}

#[derive(Debug, Deserialize, Clone)]
pub struct GeneratorConfig {
    pub generator_exe: String,
    pub generator_env: HashMap<String, String>,
    pub generator_options: Vec<String>,
    pub readonly_inputs: Vec<SyncedDir>,
    pub crashes: SyncedDir,
    pub tools: SyncedDir,

    pub target_exe: PathBuf,
    pub target_env: HashMap<String, String>,
    pub target_options: Vec<String>,
    pub target_timeout: Option<u64>,
    #[serde(default)]
    pub check_asan_log: bool,
    #[serde(default = "default_bool_true")]
    pub check_debugger: bool,
    #[serde(default)]
    pub check_retry_count: u64,
    pub rename_output: bool,
    #[serde(flatten)]
    pub common: CommonConfig,
}

pub async fn spawn(config: Arc<GeneratorConfig>) -> Result<(), Error> {
    utils::init_dir(&config.crashes.path).await?;
    utils::init_dir(&config.tools.path).await?;
    utils::sync_remote_dir(&config.tools, utils::SyncOperation::Pull).await?;
    set_executable(&config.tools.path).await?;
    let hb_client = config.common.init_heartbeat();

    for sync_dir in &config.readonly_inputs {
        utils::init_dir(&sync_dir.path).await?;
        utils::sync_remote_dir(&sync_dir, utils::SyncOperation::Pull).await?;
    }

    let resync = resync_corpuses(
        config.readonly_inputs.clone(),
        std::time::Duration::from_secs(10),
    );
    let crash_dir_monitor = utils::monitor_result_dir(config.crashes.clone());
    let tester = Tester::new(
        &config.target_exe,
        &config.target_options,
        &config.target_env,
        &config.target_timeout,
        config.check_asan_log,
        config.check_debugger,
        config.check_retry_count,
    );
    let inputs: Vec<_> = config.readonly_inputs.iter().map(|x| &x.path).collect();
    let fuzzing_monitor = start_fuzzing(&config, inputs, tester, hb_client);
    futures::try_join!(fuzzing_monitor, resync, crash_dir_monitor)?;
    Ok(())
}

async fn generate_input(
    generator_exe: &str,
    generator_env: &HashMap<String, String>,
    generator_options: &[String],
    tools_dir: impl AsRef<Path>,
    corpus_dir: impl AsRef<Path>,
    output_dir: impl AsRef<Path>,
) -> Result<()> {
    let mut expand = Expand::new();
    expand
        .generated_inputs(&output_dir)
        .input_corpus(&corpus_dir)
        .generator_exe(&generator_exe)
        .generator_options(&generator_options)
        .tools_dir(&tools_dir);

    utils::reset_tmp_dir(&output_dir).await?;

    let generator_path = Expand::new()
        .tools_dir(tools_dir.as_ref())
        .evaluate_value(generator_exe)?;

    let mut generator = Command::new(&generator_path);
    generator
        .kill_on_drop(true)
        .env_remove("RUST_LOG")
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::piped());

    for arg in expand.evaluate(generator_options)? {
        generator.arg(arg);
    }

    for (k, v) in generator_env {
        generator.env(k, expand.evaluate_value(v)?);
    }

    info!("Generating test cases with {:?}", generator);
    let output = generator.spawn()?.wait_with_output().await?;

    info!("Test case generation result {:?}", output);
    Ok(())
}

async fn start_fuzzing<'a>(
    config: &GeneratorConfig,
    corpus_dirs: Vec<impl AsRef<Path>>,
    tester: Tester<'a>,
    heartbeat_sender: Option<HeartbeatClient>,
) -> Result<()> {
    let generator_tmp = "generator_tmp";

    info!("Starting generator fuzzing loop");

    loop {
        heartbeat_sender.alive();

        for corpus_dir in &corpus_dirs {
            let corpus_dir = corpus_dir.as_ref();

            generate_input(
                &config.generator_exe,
                &config.generator_env,
                &config.generator_options,
                &config.tools.path,
                corpus_dir,
                generator_tmp,
            )
            .await?;

            let mut read_dir = fs::read_dir(generator_tmp).await?;
            while let Some(file) = read_dir.next().await {
                verbose!("Processing file {:?}", file);
                let file = file?;

                let destination_file = if config.rename_output {
                    let hash = sha256::digest_file(file.path()).await?;
                    OsString::from(hash)
                } else {
                    file.file_name()
                };

                let destination_file = config.crashes.path.join(destination_file);
                if tester.is_crash(file.path()).await? {
                    info!("Crash found, path = {}", file.path().display());

                    if let Err(err) = fs::rename(file.path(), &destination_file).await {
                        warn!("Unable to move file {:?} : {:?}", file.path(), err);
                    }
                }
            }

            verbose!(
                "Tested generated inputs for corpus = {}",
                corpus_dir.display()
            );
        }
    }
}

pub async fn resync_corpuses(dirs: Vec<SyncedDir>, delay: std::time::Duration) -> Result<()> {
    loop {
        for sync_dir in &dirs {
            utils::sync_remote_dir(sync_dir, utils::SyncOperation::Pull)
                .await
                .ok();
        }
        tokio::time::delay_for(delay).await;
    }
}

mod tests {
    #[tokio::test]
    #[cfg(target_os = "linux")]
    #[ignore]
    async fn test_radamsa_linux() {
        use super::*;
        use std::env;

        let radamsa_path = env::var("ONEFUZZ_TEST_RADAMSA_LINUX").unwrap();
        let corpus_dir_temp = tempfile::tempdir().unwrap();
        let corpus_dir = corpus_dir_temp.into_path();
        let seed_file_name = corpus_dir.clone().join("seed.txt");
        let radamsa_output_temp = tempfile::tempdir().unwrap();
        let radamsa_output = radamsa_output_temp.into_path();

        let generator_options: Vec<String> = vec![
            "-o",
            "{generated_inputs}/input-%n-%s",
            "-n",
            "100",
            "-r",
            "{input_corpus}",
        ]
        .iter()
        .map(|p| p.to_string())
        .collect();

        let radamsa_as_path = Path::new(&radamsa_path);
        let radamsa_dir = radamsa_as_path.parent().unwrap();
        let radamsa_exe = String::from("{tools_dir}/radamsa");
        let radamsa_env = HashMap::new();

        tokio::fs::write(seed_file_name, "test").await.unwrap();
        let _output = generate_input(
            &radamsa_exe,
            &radamsa_env,
            &generator_options,
            &radamsa_dir,
            corpus_dir,
            radamsa_output.clone(),
        )
        .await;
        let generated_outputs = std::fs::read_dir(radamsa_output.clone()).unwrap();
        assert_eq!(generated_outputs.count(), 100, "No crashes generated");
    }
}
