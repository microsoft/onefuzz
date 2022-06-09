// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
#![cfg_attr(target_os = "macos", allow(unused, warnings))]

use std::process::{Command, Stdio};

use anyhow::Result;
use structopt::StructOpt;

#[derive(Debug, StructOpt)]
struct Opt {
    #[structopt(min_values = 1)]
    argv: Vec<String>,

    #[structopt(short, long)]
    quiet: bool,

    #[structopt(short, long)]
    ld_library_path: Option<String>,
}

fn main() -> Result<()> {
    let opt = Opt::from_args();

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
        println!("setting LD_LIBRARY_PATH = \"{}\"", path);
        cmd.env("LD_LIBRARY_PATH", path);
    }

    let missing = find_missing(cmd)?;

    if missing.is_empty() {
        println!("no missing libraries");
    } else {
        for lib in missing {
            println!("missing library: {:x?}", lib);
        }
    }

    Ok(())
}

#[cfg(target_os = "linux")]
fn find_missing(cmd: Command) -> Result<Vec<String>> {
    Ok(dynamic_library::linux::find_missing(cmd)?
        .drain()
        .map(|m| m.name)
        .collect())
}

#[cfg(target_os = "windows")]
fn find_missing(cmd: Command) -> Result<Vec<String>> {
    Ok(dynamic_library::windows::find_missing(cmd)?
        .into_iter()
        .map(|m| m.name)
        .collect())
}

#[cfg(target_os = "macos")]
fn find_missing(cmd: Command) -> Result<Vec<String>> {
    todo!()
}
