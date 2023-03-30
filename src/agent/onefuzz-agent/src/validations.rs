use std::{collections::HashMap, path::PathBuf};

use crate::setup::SetupRunner;
use anyhow::{Context, Result};
use clap::Parser;
use onefuzz::{libfuzzer::LibFuzzer, machine_id::MachineIdentity};
use uuid::Uuid;

#[derive(Parser, Debug)]
#[clap(rename_all = "snake_case")]
pub enum ValidationCommand {
    ValidateSetup,
    ValidateLibfuzzer,
    ExecutionLog,
}

#[derive(Parser, Debug)]
#[clap(rename_all = "snake_case")]
pub struct Config {
    pub config_path: PathBuf,
    #[clap(subcommand)]
    pub command: ValidationCommand,
}

#[derive(Debug, Deserialize, Clone)]
pub struct ValidationConfig {
    pub seeds: PathBuf,
    pub setup_folder: PathBuf,
    pub target_exe: PathBuf,
    pub target_env: HashMap<String, String>,
    pub target_options: Vec<String>,
}

pub async fn validate(config: Config) -> Result<()> {
    let validation_config = serde_json::from_str::<ValidationConfig>(
        &std::fs::read_to_string(&config.config_path).context("unable to read config file")?,
    )?;

    match config.command {
        ValidationCommand::ValidateSetup => validate_setup(validation_config).await,
        ValidationCommand::ValidateLibfuzzer => validate_libfuzzer(validation_config).await,
        ValidationCommand::ExecutionLog => get_logs(validation_config).await,
    }
}

async fn validate_libfuzzer(config: ValidationConfig) -> Result<()> {
    let libfuzzer = LibFuzzer::new(
        &config.target_exe,
        config.target_options.clone(),
        config.target_env.clone(),
        config.setup_folder.clone(),
        None::<&PathBuf>,
        MachineIdentity {
            machine_id: Uuid::nil(),
            machine_name: "".to_string(),
            scaleset_name: None,
        },
    );

    libfuzzer.verify(true, Some(vec![config.seeds])).await?;
    Ok(())
}

async fn validate_setup(config: ValidationConfig) -> Result<()> {
    let output = SetupRunner::run_setup_script(config.setup_folder).await?;
    match output {
        Some(output) => {
            if !output.exit_status.success {
                let error = "error running target setup script".to_owned();
                bail!("{}", error);
            }
        }
        None => {
            println!("no setup script to run")
        }
    }
    Ok(())
}

async fn get_logs(config: ValidationConfig) -> Result<()> {
    let libfuzzer = LibFuzzer::new(
        &config.target_exe,
        config.target_options.clone(),
        config.target_env.clone(),
        config.setup_folder.clone(),
        None::<&PathBuf>,
        MachineIdentity {
            machine_id: Uuid::nil(),
            machine_name: "".to_string(),
            scaleset_name: None,
        },
    );
    let cmd = libfuzzer.build_std_command(None, None, None).await?;
    print_logs(cmd)?;
    Ok(())
}

#[cfg(target_os = "windows")]
fn print_logs(cmd: std::process::Command) -> Result<(), anyhow::Error> {
    let logs = dynamic_library::windows::get_logs(cmd)?;
    for log in logs {
        println!("{log:x?}");
    }

    Ok(())
}

#[cfg(target_os = "linux")]
fn print_logs(cmd: std::process::Command) -> Result<(), anyhow::Error> {
    let logs = dynamic_library::linux::get_linked_library_logs(&cmd)?;
    for log in logs.stdout {
        println!("{log:x?}");
    }

    let logs2 = dynamic_library::linux::get_loaded_libraries_logs(cmd)?;
    for log in logs2.stderr {
        println!("{log:x?}");
    }

    Ok(())
}

// https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/extra-tools#installation-directory
// ./gflags /i C:\setup\Xbox.Shell.OneStoreServices.FuzzerTest.exe +sls
// ./cdb -c "q" C:\setup\Xbox.Shell.OneStoreServices.FuzzerTest.exe
