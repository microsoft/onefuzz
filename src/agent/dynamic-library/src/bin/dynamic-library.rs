// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::process::{Command, Stdio};

use anyhow::Result;
use clap::Parser;

#[derive(Parser, Debug)]
struct Opt {
    #[arg(required = true, num_args = 1..)]
    argv: Vec<String>,

    #[arg(short, long)]
    quiet: bool,

    #[arg(short, long)]
    ld_library_path: Option<String>,
}

fn main() -> Result<()> {
    let opt = Opt::parse();

    let exe = &opt.argv[0];
    let mut cmd = Command::new(exe);

    if let Some(args) = opt.argv.get(1..) {
        cmd.args(args);
    }

    if opt.quiet {
        cmd.stdout(Stdio::null());
        cmd.stderr(Stdio::null());
    }

    if let Some(path) = &opt.ld_library_path {
        println!("setting LD_LIBRARY_PATH = \"{path}\"");
        cmd.env("LD_LIBRARY_PATH", path);
    }

    let (missing, errors) = find_missing(cmd)?;

    if missing.is_empty() {
        println!("no missing libraries");
    } else {
        for lib in missing {
            println!("missing library: {lib:x?}");
        }

        if !errors.is_empty() {
            println!();

            for err in errors {
                println!("error: {err}");
            }
        }
    }

    Ok(())
}

#[cfg(target_os = "linux")]
fn find_missing(cmd: Command) -> Result<MissingResult> {
    let (missing, errors) = dynamic_library::linux::find_missing(cmd)?;
    Ok((missing.drain().map(|m| m.name).collect(), errors))
}

#[cfg(target_os = "windows")]
fn find_missing(cmd: Command) -> Result<(Vec<String>, Vec<String>)> {
    let (missing, errors) = dynamic_library::windows::find_missing(cmd)?;
    Ok((missing.into_iter().map(|m| m.name).collect(), errors))
}
