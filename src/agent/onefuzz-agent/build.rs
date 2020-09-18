use std::error::Error;
use std::fs::File;
use std::io::prelude::*;
use std::process::Command;

fn run_cmd(args: &[&str]) -> Result<String, Box<dyn Error>> {
    let cmd = Command::new(args[0]).args(&args[1..]).output()?;
    if cmd.status.success() {
        Ok(String::from_utf8_lossy(&cmd.stdout).to_string())
    } else {
        Err(From::from("failed"))
    }
}

fn read_file(filename: &str) -> Result<String, Box<dyn Error>> {
    let mut file = File::open(filename)?;
    let mut contents = String::new();
    file.read_to_string(&mut contents)?;

    Ok(contents)
}

fn main() -> Result<(), Box<dyn Error>> {
    let sha = run_cmd(&["git", "rev-parse", "HEAD"])?;
    let with_changes = if run_cmd(&["git", "diff", "--quiet"]).is_err() {
        "-local_changes"
    } else {
        ""
    };
    println!("cargo:rustc-env=GIT_VERSION={}{}", sha, with_changes);

    let version = read_file("../../../CURRENT_VERSION")?;
    println!("cargo:rustc-env=ONEFUZZ_VERSION={}", version);

    Ok(())
}
