// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::tasks::{
    config::CommonConfig,
    heartbeat::{HeartbeatSender, TaskHeartbeatClient},
    utils::{self, default_bool_true, try_resolve_setup_relative_path},
};
use anyhow::{Context, Result};
use onefuzz::{
    expand::Expand,
    fs::set_executable,
    input_tester::Tester,
    process::monitor_process,
    sha256,
    syncdir::{continuous_sync, SyncOperation::Pull, SyncedDir},
};
use onefuzz_telemetry::Event::new_result;
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

impl Config {
    pub fn get_expand(&self) -> Expand<'_> {
        self
            .common
            .get_expand()
            .generator_exe(&self.generator_exe)
            .generator_options(&self.generator_options)
            .crashes(&self.crashes.local_path)
            .target_exe(&self.target_exe)
            .target_options(&self.target_options)
            .set_optional_ref(&self.tools, |expand, tools| {
                expand.tools_dir(&tools.local_path)
            })
    }
}

pub struct GeneratorTask {
    config: Config,
}

impl GeneratorTask {
    pub fn new(config: Config) -> Self {
        Self { config }
    }

    pub async fn run(&self) -> Result<()> {
        self.config.crashes.init().await.with_context(|| {
            format!(
                "creating crashes directory failed: {}",
                self.config.crashes.local_path.display()
            )
        })?;
        if let Some(tools) = &self.config.tools {
            tools.init_pull().await?;
            set_executable(&tools.local_path).await?;
        }

        let hb_client = self.config.common.init_heartbeat(None).await?;
        let jr_client = self.config.common.init_job_result().await?;

        for dir in &self.config.readonly_inputs {
            dir.init_pull().await?;
        }

        let sync_task = continuous_sync(
            &self.config.readonly_inputs,
            Pull,
            self.config.ensemble_sync_delay,
        );

        let crash_dir_monitor = self
            .config
            .crashes
            .monitor_results(new_result, false, &jr_client);

        let fuzzer = self.fuzzing_loop(hb_client);

        futures::try_join!(fuzzer, sync_task, crash_dir_monitor)?;
        Ok(())
    }

    async fn fuzzing_loop(&self, heartbeat_client: Option<TaskHeartbeatClient>) -> Result<()> {
        let target_exe =
            try_resolve_setup_relative_path(&self.config.common.setup_dir, &self.config.target_exe)
                .await?;

        let tester = Tester::new(
            &self.config.common.setup_dir,
            self.config.common.extra_setup_dir.as_deref(),
            &target_exe,
            &self.config.target_options,
            &self.config.target_env,
            self.config.common.machine_identity.clone(),
        )
        .check_asan_log(self.config.check_asan_log)
        .check_debugger(self.config.check_debugger)
        .check_retry_count(self.config.check_retry_count)
        .set_optional(self.config.target_timeout, |tester, timeout| {
            tester.timeout(timeout)
        });

        loop {
            for corpus_dir in &self.config.readonly_inputs {
                heartbeat_client.alive();
                let corpus_dir = &corpus_dir.local_path;
                let generated_inputs = tempdir()?;
                let generated_inputs_path = generated_inputs.path();

                self.generate_inputs(corpus_dir, &generated_inputs_path)
                    .await
                    .context("generate inputs failed")?;
                self.test_inputs(&generated_inputs_path, &tester)
                    .await
                    .context("test inputs failed")?;
            }
        }
    }

    async fn test_inputs(
        &self,
        generated_inputs: impl AsRef<Path>,
        tester: &Tester<'_>,
    ) -> Result<()> {
        let mut read_dir = fs::read_dir(generated_inputs).await?;
        while let Some(file) = read_dir.next_entry().await? {
            debug!("testing input: {}", file.path().display());

            let destination_file = if self.config.rename_output {
                let hash = sha256::digest_file(file.path()).await?;
                OsString::from(hash)
            } else {
                file.file_name()
            };

            let destination_file = self.config.crashes.local_path.join(destination_file);
            if tester
                .is_crash(file.path())
                .await
                .with_context(|| format!("testing input failed: {}", file.path().display()))?
            {
                fs::rename(file.path(), &destination_file).await?;
                debug!("crash found {}", destination_file.display());
            }
        }
        Ok(())
    }

    async fn generate_inputs(
        &self,
        corpus_dir: impl AsRef<Path>,
        output_dir: impl AsRef<Path>,
    ) -> Result<()> {
        utils::reset_tmp_dir(&output_dir).await?;
        let (mut generator, generator_path) = {
            let expand = self
                .config
                .get_expand()
                .generated_inputs(&output_dir)
                .input_corpus(&corpus_dir);

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
            (generator, generator_path)
        };

        info!("Generating test cases with {:?}", generator);
        let output = generator
            .spawn()
            .with_context(|| format!("generator failed to start: {generator_path}"))?;
        monitor_process(output, "generator".to_string(), true, None)
            .await
            .with_context(|| format!("generator failed to run: {generator_path}"))?;

        Ok(())
    }
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
                PlaceHolder::GeneratorExe,
                dunce::canonicalize(&self.generator_exe)
                    .unwrap()
                    .to_string_lossy()
                    .to_string(),
            ));
            params.push((
                PlaceHolder::GeneratorOptions,
                self.generator_options.join(" "),
            ));
            params.push((
                PlaceHolder::Crashes,
                dunce::canonicalize(&self.crashes.local_path)
                    .unwrap()
                    .to_string_lossy()
                    .to_string(),
            ));
            params.push((
                PlaceHolder::TargetExe,
                dunce::canonicalize(&self.target_exe)
                    .unwrap()
                    .to_string_lossy()
                    .to_string(),
            ));
            params.push((PlaceHolder::TargetOptions, self.target_options.join(" ")));
            if let Some(dir) = &self.tools {
                params.push((
                    PlaceHolder::ToolsDir,
                    dunce::canonicalize(&dir.local_path)
                        .unwrap()
                        .to_string_lossy()
                        .to_string(),
                ));
            }

            params
        }
    }

    config_test!(Config);

    #[cfg(target_os = "linux")]
    mod linux {
        use super::super::{Config, GeneratorTask};
        use crate::tasks::config::CommonConfig;
        use onefuzz::blob::BlobContainerUrl;
        use onefuzz::syncdir::SyncedDir;
        use reqwest::Url;
        use std::collections::HashMap;
        use std::env;
        use tempfile::tempdir;

        #[tokio::test]
        #[ignore]
        async fn test_radamsa_linux() -> anyhow::Result<()> {
            let crashes_temp = tempfile::tempdir()?;
            let crashes: &std::path::Path = crashes_temp.path();

            let inputs_temp = tempfile::tempdir()?;
            let inputs: &std::path::Path = inputs_temp.path();
            let input_file = inputs.join("seed.txt");
            tokio::fs::write(input_file, "test").await?;

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

            let radamsa_path = env::var("ONEFUZZ_TEST_RADAMSA_LINUX")?;
            let radamsa_as_path = std::path::Path::new(&radamsa_path);
            let radamsa_dir = radamsa_as_path.parent().unwrap();

            let readonly_inputs_local = tempfile::tempdir().unwrap().path().into();
            let crashes_local = tempfile::tempdir().unwrap().path().into();
            let tools_local = tempfile::tempdir().unwrap().path().into();
            let config = Config {
                generator_exe: String::from("{tools_dir}/radamsa"),
                generator_options,
                readonly_inputs: vec![SyncedDir {
                    local_path: readonly_inputs_local,
                    remote_path: Some(BlobContainerUrl::parse(
                        Url::from_directory_path(inputs).unwrap(),
                    )?),
                }],
                crashes: SyncedDir {
                    local_path: crashes_local,
                    remote_path: Some(BlobContainerUrl::parse(
                        Url::from_directory_path(crashes).unwrap(),
                    )?),
                },
                tools: Some(SyncedDir {
                    local_path: tools_local,
                    remote_path: Some(BlobContainerUrl::parse(
                        Url::from_directory_path(radamsa_dir).unwrap(),
                    )?),
                }),
                target_exe: Default::default(),
                target_env: Default::default(),
                target_options: Default::default(),
                target_timeout: None,
                check_asan_log: false,
                check_debugger: false,
                rename_output: false,
                ensemble_sync_delay: None,
                generator_env: HashMap::default(),
                check_retry_count: 0,
                common: Default::default(),
            };
            let task = GeneratorTask::new(config);

            let generated_inputs = tempdir()?;
            task.generate_inputs(inputs.to_path_buf(), generated_inputs.path())
                .await?;

            let count = std::fs::read_dir(generated_inputs.path())?.count();
            assert_eq!(count, 100, "No inputs generated");
            Ok(())
        }
    }
}
