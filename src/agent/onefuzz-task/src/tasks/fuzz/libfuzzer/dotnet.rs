// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use async_trait::async_trait;
use onefuzz::libfuzzer::LibFuzzer;
use tokio::process::Command;

use crate::tasks::fuzz::libfuzzer::common;

#[cfg(target_os = "linux")]
const LIBFUZZER_DOTNET_PATH: &str =
    "/onefuzz/third-party/dotnet-fuzzing-linux/libfuzzer-dotnet/libfuzzer-dotnet";

#[cfg(target_os = "windows")]
const LIBFUZZER_DOTNET_PATH: &str =
    "/onefuzz/third-party/dotnet-fuzzing-windows/libfuzzer-dotnet/libfuzzer-dotnet.exe";

#[cfg(target_os = "linux")]
const LOADER_PATH: &str =
    "/onefuzz/third-party/dotnet-fuzzing-linux/LibFuzzerDotnetLoader/LibFuzzerDotnetLoader";

#[cfg(target_os = "windows")]
const LOADER_PATH: &str =
    "/onefuzz/third-party/dotnet-fuzzing-windows/LibFuzzerDotnetLoader/LibFuzzerDotnetLoader.exe";

#[cfg(target_os = "linux")]
const SHARPFUZZ_PATH: &str =
    "/onefuzz/third-party/dotnet-fuzzing-linux/sharpfuzz/SharpFuzz.CommandLine";

#[cfg(target_os = "windows")]
const SHARPFUZZ_PATH: &str =
    "/onefuzz/third-party/dotnet-fuzzing-windows/sharpfuzz/SharpFuzz.CommandLine.exe";

#[derive(Debug)]
pub struct LibFuzzerDotnet;

#[derive(Debug, Deserialize)]
pub struct LibFuzzerDotnetConfig {
    pub target_assembly: String,
    pub target_class: String,
    pub target_method: String,
}

#[async_trait]
impl common::LibFuzzerType for LibFuzzerDotnet {
    type Config = LibFuzzerDotnetConfig;

    fn from_config(config: &common::Config<Self>) -> LibFuzzer {
        // Configure loader to fuzz user target DLL.
        let mut env = config.target_env.clone();
        env.insert(
            "LIBFUZZER_DOTNET_TARGET_ASSEMBLY".into(),
            config.extra.target_assembly.clone(),
        );
        env.insert(
            "LIBFUZZER_DOTNET_TARGET_CLASS".into(),
            config.extra.target_class.clone(),
        );
        env.insert(
            "LIBFUZZER_DOTNET_TARGET_METHOD".into(),
            config.extra.target_method.clone(),
        );

        let mut options = config.target_options.clone();
        options.push(format!("--target_path={}", LOADER_PATH));

        LibFuzzer::new(
            LIBFUZZER_DOTNET_PATH,
            options,
            env,
            &config.common.setup_dir,
        )
    }

    async fn extra_setup(config: &common::Config<Self>) -> Result<()> {
        // Use SharpFuzz to statically instrument the target assembly.
        let mut cmd = Command::new(SHARPFUZZ_PATH);
        cmd.arg(&config.extra.target_assembly);

        let mut child = cmd.spawn()?;
        let status = child.wait().await?;

        if !status.success() {
            anyhow::bail!(
                "error instrumenting assembly `{}`: {}",
                config.extra.target_assembly,
                status,
            );
        }

        Ok(())
    }
}

pub type Config = common::Config<LibFuzzerDotnet>;
pub type LibFuzzerDotnetFuzzTask = common::LibFuzzerFuzzTask<LibFuzzerDotnet>;
