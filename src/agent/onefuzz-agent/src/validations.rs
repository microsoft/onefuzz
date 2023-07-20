use std::path::{Path, PathBuf};

use crate::setup::SetupRunner;
use anyhow::Result;
use clap::Parser;
use onefuzz::{libfuzzer::LibFuzzer, machine_id::MachineIdentity};
use uuid::Uuid;

#[derive(Parser, Debug)]
#[clap(rename_all = "snake_case")]
pub enum ValidationCommand {
    /// Run the setup script
    RunSetup { setup_folder: PathBuf },
    /// Validate the libfuzzer target by attempting to run the target by itself and with some of the supplied seeds if provided
    ValidateLibfuzzer(ValidationConfig),
    /// Get the execution logs to debug dll loading issues
    ExecutionLog(ValidationConfig),
}

fn parse_key_val<T, U>(
    s: &str,
) -> Result<(T, U), Box<dyn std::error::Error + Send + Sync + 'static>>
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

#[derive(Parser, Debug, Deserialize)]
#[clap(rename_all = "snake_case")]
pub struct ValidationConfig {
    #[clap(long = "seeds")]
    pub seeds: Option<PathBuf>,
    #[clap(long = "target_exe")]
    pub target_exe: PathBuf,
    #[clap(long = "setup_folder")]
    pub setup_folder: Option<PathBuf>,
    #[clap(long = "target_options")]
    pub target_options: Vec<String>,
    #[arg(value_parser = parse_key_val::<String, String>, long = "target_env")]
    pub target_env: Vec<(String, String)>,
}

pub async fn validate(command: ValidationCommand) -> Result<()> {
    match command {
        ValidationCommand::RunSetup { setup_folder } => run_setup(setup_folder).await,
        ValidationCommand::ValidateLibfuzzer(validation_config) => {
            validate_libfuzzer(validation_config).await
        }
        ValidationCommand::ExecutionLog(validation_config) => get_logs(validation_config).await,
    }
}

async fn validate_libfuzzer(config: ValidationConfig) -> Result<()> {
    let libfuzzer = LibFuzzer::new(
        config.target_exe.clone(),
        config.target_options.clone(),
        config.target_env.iter().cloned().collect(),
        config
            .setup_folder
            .clone()
            .or_else(|| config.target_exe.parent().map(|p| p.to_path_buf()))
            .expect("invalid target_exe"),
        None,
        None,
        MachineIdentity {
            machine_id: Uuid::nil(),
            machine_name: "".to_string(),
            scaleset_name: None,
        },
    );

    if let Some(seeds) = config.seeds {
        libfuzzer.verify(true, Some(&[&seeds])).await?;
    }

    Ok(())
}

async fn run_setup(setup_folder: impl AsRef<Path>) -> Result<()> {
    let output = SetupRunner::run_setup_script(setup_folder.as_ref()).await?;
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
    let setup_folder = config
        .setup_folder
        .clone()
        .or_else(|| config.target_exe.parent().map(|p| p.to_path_buf()))
        .expect("invalid setup_folder");

    let libfuzzer = LibFuzzer::new(
        config.target_exe,
        config.target_options.clone(),
        config.target_env.iter().cloned().collect(),
        setup_folder,
        None,
        None,
        MachineIdentity {
            machine_id: Uuid::nil(),
            machine_name: String::new(),
            scaleset_name: None,
        },
    );

    let cmd = libfuzzer.build_std_command(None, None, None, None, None)?;
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
