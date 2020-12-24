use crate::tasks::config::CommonConfig;
use crate::tasks::utils::parse_key_value;
use anyhow::Result;
use clap::{App, Arg, ArgMatches};
use std::collections::HashMap;
use std::path::PathBuf;
use uuid::Uuid;

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
pub const READONLY_INPUTS: &str = "readonly_inputs_dir";
pub const CHECK_ASAN_LOG: &str = "check_asan_log";

pub const TARGET_EXE: &str = "target_exe";
pub const TARGET_ENV: &str = "target_env";
pub const TARGET_OPTIONS: &str = "target_options";
pub const SUPERVISOR_EXE: &str = "supervisor_exe";
pub const SUPERVISOR_ENV: &str = "supervisor_env";
pub const SUPERVISOR_OPTIONS: &str = "supervisor_options";
pub const GENERATOR_EXE: &str = "generator_exe";
pub const GENERATOR_ENV: &str = "generator_env";
pub const GENERATOR_OPTIONS: &str = "generator_options";

pub enum CmdType {
    Target,
    Generator,
    Supervisor,
}

pub fn add_cmd_options(
    cmd_type: CmdType,
    exe: bool,
    arg: bool,
    env: bool,
    mut app: App<'static, 'static>,
) -> App<'static, 'static> {
    let (exe_name, env_name, arg_name) = match cmd_type {
        Target => (TARGET_EXE, TARGET_ENV, TARGET_OPTIONS),
        Supervisor => (SUPERVISOR_EXE, SUPERVISOR_ENV, SUPERVISOR_OPTIONS),
        Generator => (GENERATOR_EXE, GENERATOR_ENV, GENERATOR_OPTIONS),
    };

    if exe {
        app = app.arg(Arg::with_name(exe_name).takes_value(true).required(true));
    }
    if arg {
        app = app.arg(
            Arg::with_name(env_name)
                .long(env_name)
                .takes_value(true)
                .multiple(true),
        )
    }
    if env {
        app = app.arg(
            Arg::with_name(arg_name)
                .long(arg_name)
                .takes_value(true)
                .multiple(true)
                .allow_hyphen_values(true)
                .help("Supports hyphens.  Recommendation: Set env first"),
        )
    }
    app
}

pub fn get_cmd_exe(cmd_type: CmdType, args: &clap::ArgMatches<'_>) -> Result<String> {
    let name = match cmd_type {
        Target => TARGET_EXE,
        Supervisor => SUPERVISOR_EXE,
        Generator => GENERATOR_EXE,
    };

    let exe = value_t!(args, name, String)?;
    Ok(exe)
}

pub fn get_cmd_arg(cmd_type: CmdType, args: &clap::ArgMatches<'_>) -> Vec<String> {
    let name = match cmd_type {
        Target => TARGET_OPTIONS,
        Supervisor => SUPERVISOR_OPTIONS,
        Generator => GENERATOR_OPTIONS,
    };

    args.values_of_lossy(name).unwrap_or_default()
}

pub fn get_cmd_env(
    cmd_type: CmdType,
    args: &clap::ArgMatches<'_>,
) -> Result<HashMap<String, String>> {
    let env_name = match cmd_type {
        Target => TARGET_ENV,
        Supervisor => SUPERVISOR_ENV,
        Generator => GENERATOR_ENV,
    };

    let mut env = HashMap::new();
    for opt in args.values_of_lossy(env_name).unwrap_or_default() {
        let (k, v) = parse_key_value(opt)?;
        env.insert(k, v);
    }
    Ok(env)
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
