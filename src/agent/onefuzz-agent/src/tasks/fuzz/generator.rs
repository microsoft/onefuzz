// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::{
    config::CommonConfig,
    heartbeat::*,
    utils::{self, default_bool_true},
};
use anyhow::Result;
use futures::stream::StreamExt;
use onefuzz::{
    expand::Expand,
    fs::set_executable,
    input_tester::Tester,
    process::monitor_process,
    sha256,
    syncdir::{continuous_sync, SyncOperation::Pull, SyncedDir},
    telemetry::Event::new_result,
};
use serde::Deserialize;
use std::collections::HashMap;
use std::{
    ffi::OsString,
    path::{Path, PathBuf},
    process::Stdio,
};
use tempfile::tempdir;
use tokio::{fs, process::Command};

#[derive(Debug, Deserialize, Clone)]
pub struct Config {
    pub generator_exe: String,
    pub generator_env: HashMap<String, String>,
    pub generator_options: Vec<String>,
    pub readonly_inputs: Vec<SyncedDir>,
    pub crashes: SyncedDir,
    pub tools: Option<SyncedDir>,

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
    pub ensemble_sync_delay: Option<u64>,
    #[serde(flatten)]
    pub common: CommonConfig,
}

pub struct GeneratorTask {
    config: Config,
}

impl GeneratorTask {
    pub fn new(config: Config) -> Self {
        Self { config }
    }

    pub async fn run(&self) -> Result<()> {
        self.config.crashes.init().await?;
        if let Some(tools) = &self.config.tools {
            if tools.url.is_some() {
                tools.init_pull().await?;
                set_executable(&tools.path).await?;
            }
        }

        let hb_client = self.config.common.init_heartbeat().await?;

        for dir in &self.config.readonly_inputs {
            dir.init_pull().await?;
        }

        let sync_task = continuous_sync(
            &self.config.readonly_inputs,
            Pull,
            self.config.ensemble_sync_delay,
        );

        let crash_dir_monitor = self.config.crashes.monitor_results(new_result);

        let fuzzer = self.fuzzing_loop(hb_client);

        futures::try_join!(fuzzer, sync_task, crash_dir_monitor)?;
        Ok(())
    }

    async fn fuzzing_loop(&self, heartbeat_client: Option<TaskHeartbeatClient>) -> Result<()> {
        let tester = Tester::new(
            &self.config.target_exe,
            &self.config.target_options,
            &self.config.target_env,
            &self.config.target_timeout,
            self.config.check_asan_log,
            false,
            self.config.check_debugger,
            self.config.check_retry_count,
        );

        loop {
            for corpus_dir in &self.config.readonly_inputs {
                heartbeat_client.alive();
                let corpus_dir = &corpus_dir.path;
                let generated_inputs = tempdir()?;
                let generated_inputs_path = generated_inputs.path();

                self.generate_inputs(corpus_dir, &generated_inputs_path)
                    .await?;
                self.test_inputs(&generated_inputs_path, &tester).await?;
            }
        }
    }

    async fn test_inputs(
        &self,
        generated_inputs: impl AsRef<Path>,
        tester: &Tester<'_>,
    ) -> Result<()> {
        let mut read_dir = fs::read_dir(generated_inputs).await?;
        while let Some(file) = read_dir.next().await {
            let file = file?;

            verbose!("testing input: {:?}", file);

            let destination_file = if self.config.rename_output {
                let hash = sha256::digest_file(file.path()).await?;
                OsString::from(hash)
            } else {
                file.file_name()
            };

            let destination_file = self.config.crashes.path.join(destination_file);
            if tester.is_crash(file.path()).await? {
                fs::rename(file.path(), &destination_file).await?;
                verbose!("crash found {}", destination_file.display());
            }
        }
        Ok(())
    }

    async fn generate_inputs(
        &self,
        corpus_dir: impl AsRef<Path>,
        output_dir: impl AsRef<Path>,
    ) -> Result<()> {
        let mut expand = Expand::new();
        expand
            .generated_inputs(&output_dir)
            .input_corpus(&corpus_dir)
            .generator_exe(&self.config.generator_exe)
            .generator_options(&self.config.generator_options);

        if let Some(tools) = &self.config.tools {
            expand.tools_dir(&tools.path);
        }

        utils::reset_tmp_dir(&output_dir).await?;

        let generator_path = expand.evaluate_value(&self.config.generator_exe)?;

        let mut generator = Command::new(&generator_path);
        generator
            .kill_on_drop(true)
            .env_remove("RUST_LOG")
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped());

        for arg in expand.evaluate(&self.config.generator_options)? {
            generator.arg(arg);
        }

        for (k, v) in &self.config.generator_env {
            generator.env(k, expand.evaluate_value(v)?);
        }

        info!("Generating test cases with {:?}", generator);
        let output = generator.spawn()?;
        monitor_process(output, "generator".to_string(), true, None).await?;

        Ok(())
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
