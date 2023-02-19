// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use onefuzz::monitor::DirectoryMonitor;
use clap::Parser;

#[derive(Debug, Parser)]
struct Opt {
    #[arg(short, long)]
    path: String,
}

#[tokio::main]
async fn main() -> Result<()> {
    let opt = Opt::parse();

    let mut monitor = DirectoryMonitor::new(opt.path).await?;
    monitor.set_report_directories(true);

    while let Some(created) = monitor.next_file().await? {
        println!("[create] {}", created.display());
    }

    println!("done!");

    Ok(())
}
