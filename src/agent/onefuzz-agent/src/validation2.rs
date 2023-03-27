use std::{collections::HashMap, path::PathBuf};

use anyhow::{Context, Result};
use clap::{ArgAction, Parser};
use crate::setup::SetupRunner;
use uuid::Uuid;


#[derive(Parser, Debug)]
#[clap(rename_all = "snake_case")]
pub enum ValidationCommand {
    ValidateSetup,
    ValidateLibfuzzer,
    ShowDebuggerSnap,
}

#[derive(Parser, Debug)]
#[clap(rename_all = "snake_case")]
pub struct Config {
    pub config_path: PathBuf,
    #[clap(subcommand)]
    pub command: ValidationCommand,
}

#[derive(Debug, Deserialize)]
pub struct ValidationConfig {
    pub seeds: PathBuf,
    pub setup_folder: PathBuf,
    pub target_exe: PathBuf,
    pub target_env: Vec<(String, String)>,
    pub target_options: Vec<String>,
}


pub async fn validate(config: Config) -> Result<()> {
    let validation_config = serde_json::from_str::<ValidationConfig>(
        &std::fs::read_to_string(&config.config_path)
            .context("unable to read config file")?,
    )?;


    match config.command {
        ValidationCommand::ValidateSetup => validate_setup(validation_config).await,
        ValidationCommand::ValidateLibfuzzer => validate_libfuzzer().await,
        ValidationCommand::ShowDebuggerSnap => show_debugger_snap().await,
    }
}

async fn show_debugger_snap() -> Result<()> {
    Ok(())
}

async fn validate_libfuzzer() -> Result<()> {
    Ok(())
}

async fn validate_setup(config: ValidationConfig) -> Result<()> {

    let result = SetupRunner::run_setup_script(config.setup_folder).await?;
    // SetupRunner::run_setup(&config.se, &config.extra_folder).await?;
    Ok(())
}
