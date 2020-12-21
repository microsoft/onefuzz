use crate::tasks::config::CommonConfig;
use crate::tasks::utils::parse_key_value;
use anyhow::Result;
use clap::{App, Arg, ArgMatches};
use std::collections::HashMap;
use uuid::Uuid;

pub const TARGET_EXE: &str = "target_exe";
const TARGET_ENV: &str = "target_env";
pub const TARGET_OPTIONS: &str = "target_options";
pub const INPUTS_DIR: &str = "inputs_dir";
pub const CRASHES_DIR: &str = "crashes_dir";
pub const TARGET_WORKERS: &str = "target_workers";
pub const REPORTS_DIR: &str = "reports_dir";
pub const NO_REPRO_DIR: &str = "no_repro_dir";
pub const TARGET_TIMEOUT: &str = "target_timeout";
pub const CHECK_RETRY_COUNT: &str = "check_retry_count";
pub const DISABLE_CHECK_QUEUE: &str = "disable_check_queue";
pub const UNIQUE_REPORTS_DIR: &str = "unique_reports_dir";
pub const COVERAGE_DIR: &str = "coverage_dir";

pub fn add_target_cmd_options(
    exe: bool,
    arg: bool,
    env: bool,
    mut app: App<'static, 'static>,
) -> App<'static, 'static> {
    if exe {
        app = app.arg(Arg::with_name(TARGET_EXE).takes_value(true).required(true));
    }
    if arg {
        app = app.arg(
            Arg::with_name(TARGET_ENV)
                .long(TARGET_ENV)
                .takes_value(true)
                .multiple(true),
        )
    }
    if env {
        app = app.arg(
            Arg::with_name(TARGET_OPTIONS)
                .long(TARGET_OPTIONS)
                .takes_value(true)
                .multiple(true)
                .allow_hyphen_values(true)
                .help("Supports hyphens.  Recommendation: Set target_env first"),
        )
    }
    app
}

pub fn get_target_env(args: &clap::ArgMatches<'_>) -> Result<HashMap<String, String>> {
    let mut target_env = HashMap::new();
    for opt in args.values_of_lossy("target_env").unwrap_or_default() {
        let (k, v) = parse_key_value(opt)?;
        target_env.insert(k, v);
    }
    Ok(target_env)
}

pub fn add_common_config(app: App<'static, 'static>) -> App<'static, 'static> {
    app.arg(
        Arg::with_name("job_id")
            .long("job_id")
            .takes_value(true)
            .required(false),
    )
    .arg(
        Arg::with_name("task_id")
            .long("task_id")
            .takes_value(true)
            .required(false),
    )
    .arg(
        Arg::with_name("instance_id")
            .long("instance_id")
            .takes_value(true)
            .required(false),
    )
}

fn get_uuid(name: &str, args: &ArgMatches<'_>) -> Result<Uuid> {
    match value_t!(args, name, String) {
        Ok(x) => Uuid::parse_str(&x)
            .map_err(|x| format_err!("invalid {}.  uuid expected.  {})", name, x)),
        Err(_) => Ok(Uuid::nil()),
    }
}

pub fn build_common_config(args: &ArgMatches<'_>) -> Result<CommonConfig> {
    let job_id = get_uuid("job_id", args)?;
    let task_id = get_uuid("task_id", args)?;
    let instance_id = get_uuid("instance_id", args)?;

    let config = CommonConfig {
        heartbeat_queue: None,
        instrumentation_key: None,
        telemetry_key: None,
        job_id,
        task_id,
        instance_id,
    };
    Ok(config)
}
