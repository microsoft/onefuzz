use crate::tasks::config::CommonConfig;
use anyhow::Result;
use clap::{App, Arg, ArgMatches};
use uuid::Uuid;

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
