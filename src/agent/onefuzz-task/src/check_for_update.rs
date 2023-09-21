use std::process::Stdio;

use anyhow::Result;
use serde_json::Value;

pub fn run(onefuzz_built_version: &str) -> Result<()> {
    // Find onefuzz cli
    let common_names = ["onefuzz", "onefuzz.exe", "onefuzz.cmd"];
    let mut valid_commands: Vec<_> = common_names
        .into_iter()
        .map(|name| {
            (
                name,
                std::process::Command::new(name)
                    .stderr(Stdio::null())
                    .stdout(Stdio::null())
                    .arg("-h")
                    .spawn(),
            )
        })
        .filter_map(|(name, child)| child.ok().map(|c| (name, c)))
        .collect();

    if valid_commands.is_empty() {
        bail!(
            "Could not find any of the following common names for the onefuzz-cli: {:?}",
            common_names
        );
    }

    let (name, child) = valid_commands
        .first_mut()
        .expect("Expected valid_commands to not be empty");

    info!("Found the onefuzz cli at: {}", name);

    // We just used this to check if it exists, we'll invoke it again later
    let _ = child.kill();

    // Run onefuzz info get
    let output = std::process::Command::new(&name)
        .args(["info", "get"])
        .output()?;

    if !output.status.success() {
        bail!(
            "Failed to run command `{} info get`. stderr: {:?}, stdout: {:?}",
            name,
            String::from_utf8(output.stderr),
            String::from_utf8(output.stdout)
        )
    }

    let stdout = String::from_utf8(output.stdout)?;
    let info: Value = serde_json::from_str(&stdout)?;

    if let Some(onefuzz_service_version) = info["versions"]["onefuzz"]["version"].as_str() {
        if onefuzz_service_version == onefuzz_built_version {
            println!("You are up to date!");
        } else {
            println!(
                "Version mismatch. onefuzz-task version: {} | onefuzz service version: {}",
                onefuzz_built_version, onefuzz_service_version
            );
            println!(
                "To update, please run the following command: {} tools get .",
                name
            );
            println!("Then extract the onefuzz-task binary from the appropriate OS folder");
        }
        return Ok(());
    }

    bail!(
        "Failed to get onefuzz service version from cli response: {}",
        stdout
    )
}
