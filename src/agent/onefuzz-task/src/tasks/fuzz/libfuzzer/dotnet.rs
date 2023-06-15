// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::PathBuf;

use anyhow::Result;
use async_trait::async_trait;
use onefuzz::fs::set_executable;
use onefuzz::libfuzzer::LibFuzzer;
use onefuzz::syncdir::SyncedDir;
use tokio::process::Command;

use crate::tasks::fuzz::libfuzzer::common;
use crate::tasks::utils::try_resolve_setup_relative_path;

#[cfg(target_os = "linux")]
const LIBFUZZER_DOTNET_PATH: &str = "libfuzzer-dotnet/libfuzzer-dotnet";

#[cfg(target_os = "windows")]
const LIBFUZZER_DOTNET_PATH: &str = "libfuzzer-dotnet/libfuzzer-dotnet.exe";

#[cfg(target_os = "linux")]
const LOADER_PATH: &str = "LibFuzzerDotnetLoader/LibFuzzerDotnetLoader";

#[cfg(target_os = "windows")]
const LOADER_PATH: &str = "LibFuzzerDotnetLoader/LibFuzzerDotnetLoader.exe";

#[cfg(target_os = "linux")]
const SHARPFUZZ_PATH: &str = "sharpfuzz/SharpFuzz.CommandLine";

#[cfg(target_os = "windows")]
const SHARPFUZZ_PATH: &str = "sharpfuzz/SharpFuzz.CommandLine.exe";

#[derive(Debug)]
pub struct LibFuzzerDotnet;

#[derive(Debug, Deserialize)]
pub struct LibFuzzerDotnetConfig {
    pub target_assembly: String,
    pub target_class: String,
    pub target_method: String,
    pub tools: SyncedDir,
}

impl LibFuzzerDotnetConfig {
    fn libfuzzer_dotnet_path(&self) -> PathBuf {
        self.tools.local_path.join(LIBFUZZER_DOTNET_PATH)
    }

    fn loader_path(&self) -> PathBuf {
        self.tools.local_path.join(LOADER_PATH)
    }

    fn sharpfuzz_path(&self) -> PathBuf {
        self.tools.local_path.join(SHARPFUZZ_PATH)
    }
}

#[async_trait]
impl common::LibFuzzerType for LibFuzzerDotnet {
    type Config = LibFuzzerDotnetConfig;

    async fn from_config(config: &common::Config<Self>) -> Result<LibFuzzer> {
        let target_assembly = config.target_assembly().await?;

        // Configure loader to fuzz user target DLL.
        let mut env = config.target_env.clone();
        env.insert("LIBFUZZER_DOTNET_TARGET_ASSEMBLY".into(), target_assembly);
        env.insert(
            "LIBFUZZER_DOTNET_TARGET_CLASS".into(),
            config.extra.target_class.clone(),
        );
        env.insert(
            "LIBFUZZER_DOTNET_TARGET_METHOD".into(),
            config.extra.target_method.clone(),
        );

        let mut options = config.target_options.clone();
        options.push(format!(
            "--target_path={}",
            config.extra.loader_path().display()
        ));

        Ok(LibFuzzer::new(
            config.extra.libfuzzer_dotnet_path(),
            options,
            env,
            config.common.setup_dir.clone(),
            config.common.extra_setup_dir.clone(),
            config
                .common
                .extra_output
                .as_ref()
                .map(|x| x.local_path.clone()),
            config.common.machine_identity.clone(),
        ))
    }

    async fn extra_setup(config: &common::Config<Self>) -> Result<()> {
        // Download dotnet fuzzing tools.
        config.extra.tools.init_pull().await?;

        // Ensure tools are executable.
        set_executable(&config.extra.tools.local_path).await?;

        // Use SharpFuzz to statically instrument the target assembly.
        let mut cmd = Command::new(config.extra.sharpfuzz_path());

        let target_assembly = config.target_assembly().await?;
        cmd.arg(target_assembly);

        cmd.stdout(std::process::Stdio::piped());
        cmd.stderr(std::process::Stdio::piped());

        let child = cmd.spawn()?;
        let output = child.wait_with_output().await?;

        if !output.status.success() {
            anyhow::bail!(
                "error instrumenting assembly `{}`: {:?}",
                config.extra.target_assembly,
                output,
            );
        }

        Ok(())
    }
}

impl common::Config<LibFuzzerDotnet> {
    async fn target_assembly(&self) -> Result<String> {
        let resolved =
            try_resolve_setup_relative_path(&self.common.setup_dir, &self.extra.target_assembly)
                .await?
                .to_string_lossy()
                .into_owned();

        Ok(resolved)
    }
}

pub type Config = common::Config<LibFuzzerDotnet>;
pub type LibFuzzerDotnetFuzzTask = common::LibFuzzerFuzzTask<LibFuzzerDotnet>;
