use std::process::Command;
use std::sync::Arc;
use std::time::Duration;

use anyhow::{bail, Result};
use clap::Parser;
use cobertura::CoberturaCoverage;
use coverage::allowlist::{AllowList, TargetAllowList};
use coverage::binary::BinaryCoverage;
use coverage::record::{CoverageRecorder, Recorded};
use debuggable_module::loader::Loader;

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

    #[arg(short = 'd', long)]
    input_dir: Option<String>,

    #[arg(required = true, num_args = 1..)]
    command: Vec<String>,
}

#[derive(Debug, Copy, Clone, PartialEq, Eq, clap::ValueEnum)]
enum OutputFormat {
    ModOff,
    Source,
    Cobertura,
}

const DEFAULT_TIMEOUT: Duration = Duration::from_secs(5);
const INPUT_MARKER: &str = "@@";

fn main() -> Result<()> {
    let args = Args::parse();

    env_logger::init();

    let timeout = args
        .timeout
        .map(Duration::from_millis)
        .unwrap_or(DEFAULT_TIMEOUT);

    let mut allowlist = TargetAllowList::default();

    if let Some(path) = &args.module_allowlist {
        allowlist.modules = AllowList::load(path)?;
    }

    if let Some(path) = &args.source_allowlist {
        allowlist.source_files = AllowList::load(path)?;
    }

    let mut coverage = BinaryCoverage::default();
    let loader = Arc::new(Loader::new());

    if let Some(dir) = args.input_dir {
        check_for_input_marker(&args.command)?;

        for input in std::fs::read_dir(dir)? {
            let input = input?.path();
            let cmd = command(&args.command, Some(&input.to_string_lossy()));

            let recorded = CoverageRecorder::new(cmd)
                .allowlist(allowlist.clone())
                .loader(loader.clone())
                .timeout(timeout)
                .record()?;

            if args.dump_stdio {
                dump_stdio(&recorded);
            }

            coverage.merge(&recorded.coverage);
        }
    } else {
        let cmd = command(&args.command, None);
        let recorded = CoverageRecorder::new(cmd)
            .allowlist(allowlist)
            .loader(loader)
            .timeout(timeout)
            .record()?;

        if args.dump_stdio {
            dump_stdio(&recorded);
        }

        coverage.merge(&recorded.coverage);
    }

    match args.output {
        OutputFormat::ModOff => dump_modoff(&coverage)?,
        OutputFormat::Source => dump_source_line(&coverage)?,
        OutputFormat::Cobertura => dump_cobertura(&coverage)?,
    }

    Ok(())
}

fn check_for_input_marker(argv: &[String]) -> Result<()> {
    // Skip exe name, require input marker in args.
    for arg in argv.iter().skip(1) {
        if arg.contains(INPUT_MARKER) {
            return Ok(());
        }
    }

    bail!("input file template string not present in target args")
}

fn command(argv: &[String], input: Option<&str>) -> Command {
    let mut cmd = Command::new(&argv[0]);

    let args = argv.iter().skip(1);

    if let Some(input) = input {
        cmd.args(args.map(|a| a.replace(INPUT_MARKER, input)));
    } else {
        cmd.args(args);
    }

    cmd
}

fn dump_stdio(recorded: &Recorded) {
    if let Some(status) = recorded.output.status {
        println!("status = {status}");
    } else {
        println!("status = <unavailable>");
    }
    println!("stderr =========================================================================");
    println!("{}", recorded.output.stderr);
    println!("stdout =========================================================================");
    println!("{}", recorded.output.stdout);
    println!();
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

#[cfg(test)]
mod test {
    use super::*;

    #[test]
    fn can_run_coverage() {
        #[cfg(target_os = "linux")]
        let cmd = command(&["ls"].map(str::to_string), None);

        #[cfg(target_os = "windows")]
        let cmd = command(&["cmd.exe", "/c", "dir"].map(str::to_string), None);

        let recorded = CoverageRecorder::new(cmd)
            .timeout(Duration::from_secs(5))
            .record()
            .unwrap();

        assert_ne!("", recorded.output.stdout);

        // only non-debuggable modules are found on Windows
        #[cfg(target_os = "linux")]
        assert!(recorded.coverage.modules.len() > 0);
    }
}
