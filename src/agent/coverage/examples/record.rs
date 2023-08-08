use std::process::Command;
use std::sync::Arc;
use std::time::Duration;

use anyhow::{bail, Context, Result};
use clap::Parser;
use cobertura::CoberturaCoverage;
use coverage::allowlist::AllowList;
use coverage::binary::{BinaryCoverage, DebugInfoCache};
use coverage::record::{CoverageRecorder, Recorded};
use debuggable_module::load_module::LoadModule;
use debuggable_module::loader::Loader;
use debuggable_module::path::FilePath;
use debuggable_module::Module;
use itertools::Itertools;

#[derive(Parser, Debug)]
struct Args {
    #[arg(long)]
    module_allowlist: Option<String>,

    #[arg(long)]
    source_allowlist: Option<String>,

    #[arg(short, long, help = "The target timeout, in milliseconds.")]
    timeout: Option<u64>,

    #[arg(short, long, value_enum, default_value_t = OutputFormat::ModOff)]
    output: OutputFormat,

    #[arg(long)]
    dump_stdio: bool,

    #[arg(
        short = 'd',
        long,
        help = "The files in this directory will be iterated and placed in '@@' in the command string."
    )]
    input_dir: Option<String>,

    #[arg(required = true, num_args = 1.., help="The command to record coverage for. This should contain the replacement string '@@' if input_dir is used.")]
    command: Vec<String>,

    #[arg(
        long,
        requires = "input_dir",
        help = "Pass multiple files from input_dir instead of one at a time. This works with LibFuzzer-based fuzzers."
    )]
    batch_inputs: bool,
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

    let module_allowlist = args
        .module_allowlist
        .as_ref()
        .map(AllowList::load)
        .unwrap_or_else(|| Ok(AllowList::default()))
        .context("loading module allowlist")?;

    let source_allowlist = args
        .source_allowlist
        .as_ref()
        .map(AllowList::load)
        .unwrap_or_else(|| Ok(AllowList::default()))
        .context("loading source allowlist")?;

    let mut coverage = BinaryCoverage::default();
    let loader = Arc::new(Loader::new());
    let cache = Arc::new(DebugInfoCache::new(source_allowlist.clone()));

    let t = std::time::Instant::now();
    precache_target(&args.command[0], &loader, &cache)?;
    log::info!("precached: {:?}", t.elapsed());

    for cmd in generate_commands(&args)? {
        let t = std::time::Instant::now();

        let recorded = CoverageRecorder::new(cmd)
            .module_allowlist(module_allowlist.clone())
            .loader(loader.clone())
            .debuginfo_cache(cache.clone())
            .timeout(timeout)
            .record()
            .context("recording coverage")?;

        log::info!("recorded: {:?}", t.elapsed());

        if args.dump_stdio {
            dump_stdio(&recorded);
        }

        coverage.merge(&recorded.coverage);
    }

    match args.output {
        OutputFormat::ModOff => dump_modoff(&coverage)?,
        OutputFormat::Source => dump_source_line(&coverage, source_allowlist)?,
        OutputFormat::Cobertura => dump_cobertura(&coverage, source_allowlist)?,
    }

    Ok(())
}

fn generate_commands(args: &Args) -> Result<Box<dyn Iterator<Item = Command> + '_>> {
    if let Some(dir) = &args.input_dir {
        check_for_input_marker(&args.command)?;

        let files = std::fs::read_dir(dir)
            .context("reading input_dir")?
            .filter_map(|r| match r {
                Ok(entry) => Some(entry.path().to_string_lossy().into_owned()),
                Err(err) => {
                    log::warn!("error reading file entry, skipping it: {err}");
                    None
                }
            });

        if args.batch_inputs {
            Ok(Box::new(
                files
                    .peekable()
                    .batching(|it| -> Option<Vec<String>> {
                        // batch up arguments until we have up to this many bytes:
                        const MAX_ARG_SIZE: usize = 8000;

                        let mut result = Vec::new();
                        let mut arg_len = 0;

                        while let Some(next) =
                            it.next_if(|next| (arg_len + next.len()) <= MAX_ARG_SIZE)
                        {
                            arg_len += next.len();
                            result.push(next);
                        }

                        if !result.is_empty() {
                            Some(result)
                        } else {
                            None
                        }
                    })
                    .map(|paths| command(&args.command, Some(&paths))),
            ))
        } else {
            Ok(Box::new(
                files.map(|path| command(&args.command, Some(&[path]))),
            ))
        }
    } else {
        Ok(Box::new([command(&args.command, None)].into_iter()))
    }
}

fn precache_target(exe: &str, loader: &Loader, cache: &DebugInfoCache) -> Result<()> {
    // Debugger tracks modules as absolute paths.
    let exe = std::fs::canonicalize(exe)
        .with_context(|| format!("finding {:?}", exe))?
        .display()
        .to_string();

    let exe = FilePath::new(exe)?;

    // Eagerly analyze target debuginfo.
    let module: Box<dyn Module> = LoadModule::load(loader, exe)?;
    cache.get_or_insert(&*module)?;

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

fn command(argv: &[String], inputs: Option<&[String]>) -> Command {
    let mut cmd = Command::new(&argv[0]);

    let args = argv.iter().skip(1);

    if let Some(inputs) = inputs {
        let mut expanded_args = Vec::with_capacity(argv.len() + inputs.len());

        for arg in args {
            if arg.contains(INPUT_MARKER) {
                for input in inputs {
                    expanded_args.push(arg.replace(INPUT_MARKER, input));
                }
            } else {
                expanded_args.push(arg.to_string());
            }
        }

        cmd.args(expanded_args);
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

fn dump_source_line(binary: &BinaryCoverage, allowlist: AllowList) -> Result<()> {
    let source = coverage::source::binary_to_source_coverage(binary, allowlist)?;

    for (path, file) in &source.files {
        for (line, count) in &file.lines {
            println!("{}:{} {}", path, line.number(), count.0);
        }
    }

    Ok(())
}

fn dump_cobertura(binary: &BinaryCoverage, allowlist: AllowList) -> Result<()> {
    let source = coverage::source::binary_to_source_coverage(binary, allowlist)?;
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
