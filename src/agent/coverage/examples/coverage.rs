use std::process::Command;
use std::time::Duration;

use anyhow::Result;
use clap::Parser;
use coverage::allowlist::{AllowList, TargetAllowList};
use coverage::binary::BinaryCoverage;
use coverage::record::CoverageRecorder;
use debuggable_module::loader::Loader;
use onefuzz_file_format::coverage::cobertura::CoberturaCoverage;

#[derive(Parser, Debug)]
struct Args {
    #[arg(long)]
    module_allowlist: Option<String>,

    #[arg(long)]
    source_allowlist: Option<String>,

    #[arg(short, long)]
    timeout: Option<u64>,

    #[arg(short, long, value_enum, default_value_t = OutputFormat::ModOff)]
    output: OutputFormat,

    #[arg(long)]
    dump_stdio: bool,

    command: Vec<String>,
}

#[derive(Copy, Clone, PartialEq, Eq, clap::ValueEnum)]
enum OutputFormat {
    ModOff,
    Source,
    Cobertura,
}

const DEFAULT_TIMEOUT: Duration = Duration::from_secs(5);

fn main() -> Result<()> {
    let args = Args::parse();

    env_logger::init();

    let timeout = args
        .timeout
        .map(Duration::from_millis)
        .unwrap_or(DEFAULT_TIMEOUT);

    let mut cmd = Command::new(&args.command[0]);
    if args.command.len() > 1 {
        cmd.args(&args.command[1..]);
    }

    let mut allowlist = TargetAllowList::default();

    if let Some(path) = &args.module_allowlist {
        allowlist.modules = AllowList::load(path)?;
    }

    if let Some(path) = &args.source_allowlist {
        allowlist.source_files = AllowList::load(path)?;
    }

    let loader = Loader::new();
    let recorded = CoverageRecorder::new(cmd)
        .allowlist(allowlist)
        .loader(loader)
        .timeout(timeout)
        .record()?;

    if args.dump_stdio {
        if let Some(status) = &recorded.output.status {
            println!("status = {status}");
        } else {
            println!("status = <unavailable>");
        }
        println!(
            "stderr ========================================================================="
        );
        println!("{}", recorded.output.stderr);
        println!(
            "stdout ========================================================================="
        );
        println!("{}", recorded.output.stdout);
        println!();
    }

    match args.output {
        OutputFormat::ModOff => dump_modoff(&recorded.coverage)?,
        OutputFormat::Source => dump_source_line(&recorded.coverage)?,
        OutputFormat::Cobertura => dump_cobertura(&recorded.coverage)?,
    }

    Ok(())
}

fn dump_modoff(coverage: &BinaryCoverage) -> Result<()> {
    for (module, coverage) in &coverage.modules {
        for (offset, count) in coverage.as_ref() {
            if count.reached() {
                println!("{}+{offset:x}", module.base_name());
            }
        }
    }

    Ok(())
}

fn dump_source_line(binary: &BinaryCoverage) -> Result<()> {
    let source = coverage::source::binary_to_source_coverage(binary)?;

    for (path, file) in &source.files {
        for (line, count) in &file.lines {
            println!("{}:{} {}", path, line.number(), count.0);
        }
    }

    Ok(())
}

fn dump_cobertura(binary: &BinaryCoverage) -> Result<()> {
    let source = coverage::source::binary_to_source_coverage(binary)?;
    let cobertura: CoberturaCoverage = source.into();

    println!("{}", cobertura.to_string()?);

    Ok(())
}
