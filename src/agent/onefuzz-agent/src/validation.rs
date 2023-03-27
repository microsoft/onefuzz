use std::{path::PathBuf, collections::HashMap};

use anyhow::{Context, Result};
use clap::{ArgAction, Parser};

#[derive(Parser, Debug)]
#[clap(rename_all = "snake_case")]
pub enum ValidationCommand{
    ValidateSetup,
    ValidateLibfuzzer,
    ShowDebuggerSnap
}

#[derive(Parser, Debug)]
pub struct ValidationConfig {
    pub seeds: PathBuf,
    pub setup_folder: PathBuf,
    pub target_exe: PathBuf,
    #[arg(short = 'D', value_parser = parse_key_val::<String, String>)]
    pub target_env: Vec<(String, String)>,
    pub target_options: Vec<String>,
}

fn parse_key_val<T, U>(s: &str) -> Result<(T, U), Box<dyn std::error::Error + Send + Sync + 'static>>
where
    T: std::str::FromStr,
    T::Err: std::error::Error + Send + Sync + 'static,
    U: std::str::FromStr,
    U::Err: std::error::Error + Send + Sync + 'static,
{
    let pos = s
        .find('=')
        .ok_or_else(|| format!("invalid KEY=value: no `=` found in `{s}`"))?;
    Ok((s[..pos].parse()?, s[pos + 1..].parse()?))
}


pub async fn validate(validation_command: ValidationCommand) -> Result<()> {
    match validation_command {
        ValidationCommand::ValidateSetup => {
            validate_setup().await
        },
        ValidationCommand::ValidateLibfuzzer => {
            validate_libfuzzer().await
        },
        ValidationCommand::ShowDebuggerSnap => {
            show_debugger_snap().await
        }
    }
}

async fn show_debugger_snap() -> Result<()> {
    todo!()
}

async fn validate_libfuzzer() -> Result<()> {
    todo!()
}

async fn validate_setup() -> Result<()> {
    todo!()
}