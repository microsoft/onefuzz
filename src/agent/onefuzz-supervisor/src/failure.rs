use anyhow::{Context, Error, Result};
use onefuzz::fs::onefuzz_root;
use std::fs;
use std::path::PathBuf;

const FAILURE_FILE: &str = "onefuzz-supervisor-failure.txt";

pub fn failure_path() -> Result<PathBuf> {
    Ok(onefuzz_root()?.join(FAILURE_FILE))
}

pub fn save_failure(err: &Error) -> Result<()> {
    error!("saving failure: {:?}", err);
    let path = failure_path()?;
    let message = format!("{:?}", err);
    fs::write(&path, message)
        .with_context(|| format!("unable to write failure log: {}", path.display()))
}

pub fn read_failure() -> Result<String> {
    let path = failure_path()?;
    fs::read_to_string(&path)
        .with_context(|| format!("unable to read failure log: {}", path.display()))
}
