// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use stacktrace_parser::CrashLog;
use std::{env, fs};

fn main() -> Result<()> {
    for filename in env::args().skip(1) {
        let data = fs::read_to_string(&filename)?;
        let asan = CrashLog::parse(data)?;
        eprintln!("{}", filename);
        println!("{}", serde_json::to_string_pretty(&asan)?);
    }

    Ok(())
}
