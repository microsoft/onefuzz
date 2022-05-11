// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use onefuzz::monitor::DirectoryMonitor;
use structopt::StructOpt;

#[derive(Debug, StructOpt)]
struct Opt {
    #[structopt(short, long)]
    path: String,
}

#[tokio::main]
async fn main() -> Result<()> {
    let opt = Opt::from_args();

    let mut monitor = DirectoryMonitor::new(opt.path)
        .await?
        .set_report_directories(true);

    while let Some(created) = monitor.next_file().await? {
        println!("[create] {}", created.display());
    }

    println!("done!");

    Ok(())
}
