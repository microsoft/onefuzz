use anyhow::{Context, Result};
use clap::{ArgAction, Parser};

#[derive(Parser, Debug)]
#[clap(rename_all = "snake_case")]
pub enum ValidationCommand{
    ValidateSetup,
    ValidateLibfuzzer,
    ShowDebuggerSnap
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