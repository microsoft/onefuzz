// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::{
    local::common::build_common_config,
    tasks::{
        report::libfuzzer_report::{Config, ReportTask},
        utils::parse_key_value,
    },
};
use anyhow::Result;
use clap::{App, Arg, SubCommand};
use onefuzz::{blob::BlobContainerUrl, syncdir::SyncedDir};
use std::{collections::HashMap, path::PathBuf};
use url::Url;

pub async fn run(args: &clap::ArgMatches<'_>) -> Result<()> {
    let target_exe = value_t!(args, "target_exe", PathBuf)?;
    let crashes_dir = value_t!(args, "crashes_dir", PathBuf)?;
    let reports_dir = value_t!(args, "reports_dir", PathBuf)?;

    let target_options = args.values_of_lossy("target_options").unwrap_or_default();
    let mut target_env = HashMap::new();
    for opt in args.values_of_lossy("target_env").unwrap_or_default() {
        let (k, v) = parse_key_value(opt)?;
        target_env.insert(k, v);
    }
    let target_timeout = value_t!(args, "target_timeout", u64).ok();
    let check_retry_count = value_t!(args, "check_retry_count", u64)?;

    let common = build_common_config(args)?;
    let config = Config {
        target_exe,
        target_env,
        target_options,
        target_timeout,
        check_retry_count,
        input_queue: None,
        crashes: Some(SyncedDir {
            path: crashes_dir,
            url: BlobContainerUrl::new(Url::parse("https://contoso.com/crashes")?)?,
        }),
        reports: None,
        no_repro: None,
        unique_reports: SyncedDir {
            path: reports_dir,
            url: BlobContainerUrl::new(Url::parse("https://contoso.com/unique_reports")?)?,
        },
        common,
    };

    ReportTask::new(config).run_local().await
}

pub fn args(name: &'static str) -> App<'static, 'static> {
    SubCommand::with_name(name)
        .about("execute a local-only libfuzzer crash report task")
        .arg(
            Arg::with_name("target_exe")
                .takes_value(true)
                .required(true),
        )
        .arg(
            Arg::with_name("crashes_dir")
                .takes_value(true)
                .required(true),
        )
        .arg(
            Arg::with_name("reports_dir")
                .takes_value(true)
                .required(true),
        )
        .arg(
            Arg::with_name("target_env")
                .long("target_env")
                .takes_value(true)
                .multiple(true),
        )
        .arg(
            Arg::with_name("target_options")
                .long("target_options")
                .takes_value(true)
                .multiple(true)
                .allow_hyphen_values(true)
                .help("Supports hyphens.  Recommendation: Set target_env first"),
        )
        .arg(
            Arg::with_name("target_timeout")
                .takes_value(true)
                .long("target_timeout"),
        )
        .arg(
            Arg::with_name("check_retry_count")
                .takes_value(true)
                .long("check_retry_count")
                .default_value("0"),
        )
}
