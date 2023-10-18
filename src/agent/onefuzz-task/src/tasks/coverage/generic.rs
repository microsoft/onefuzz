// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::HashMap;
use std::convert::TryFrom;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::sync::Arc;
use std::time::Duration;

use anyhow::{bail, Context, Result};
use async_trait::async_trait;
use cobertura::{CoberturaCoverage, WriteXml};
use coverage::allowlist::AllowList;
use coverage::binary::{BinaryCoverage, DebugInfoCache};
use coverage::record::CoverageRecorder;
use coverage::source::{binary_to_source_coverage, SourceCoverage};
use debuggable_module::load_module::LoadModule;
use debuggable_module::loader::Loader;
use debuggable_module::path::FilePath;
use debuggable_module::Module;
use onefuzz::env::LD_LIBRARY_PATH;
use onefuzz::expand::{Expand, PlaceHolder};
use onefuzz::syncdir::SyncedDir;
use onefuzz_file_format::coverage::{
    binary::{v1::BinaryCoverageJson as BinaryCoverageJsonV1, BinaryCoverageJson},
    source::{v1::SourceCoverageJson as SourceCoverageJsonV1, SourceCoverageJson},
};
use onefuzz_result::job_result::JobResultData;
use onefuzz_result::job_result::{JobResultSender, TaskJobResultClient};
use onefuzz_telemetry::{event, warn, Event::coverage_data, Event::coverage_failed, EventData};
use storage_queue::{Message, QueueClient};
use tokio::fs;
use tokio::sync::RwLock;
use tokio::task::spawn_blocking;
use tokio_stream::wrappers::ReadDirStream;
use url::Url;

use crate::tasks::config::CommonConfig;
use crate::tasks::generic::input_poller::{CallbackImpl, InputPoller, Processor};
use crate::tasks::heartbeat::{HeartbeatSender, TaskHeartbeatClient};
use crate::tasks::utils::try_resolve_setup_relative_path;

use super::COBERTURA_COVERAGE_FILE;

const MAX_COVERAGE_RECORDING_ATTEMPTS: usize = 2;
const COVERAGE_FILE: &str = "coverage.json";
const SOURCE_COVERAGE_FILE: &str = "source-coverage.json";

const DEFAULT_TARGET_TIMEOUT: Duration = Duration::from_secs(120);

const WINDOWS_INTERCEPTOR_DENYLIST: &str = include_str!("generic/windows-interceptor.list");

#[derive(Debug, Deserialize)]
pub struct Config {
    pub target_exe: PathBuf,
    pub target_env: HashMap<String, String>,
    pub target_options: Vec<String>,
    pub target_timeout: Option<u64>,

    // Deprecated.
    //
    // Retained only to informatively fail tasks that were qeueued pre-upgrade.
    pub coverage_filter: Option<String>,

    pub module_allowlist: Option<String>,
    pub source_allowlist: Option<String>,

    pub input_queue: Option<QueueClient>,
    pub readonly_inputs: Vec<SyncedDir>,
    pub coverage: SyncedDir,

    #[serde(flatten)]
    pub common: CommonConfig,
}

impl Config {
    pub fn timeout(&self) -> Duration {
        self.target_timeout
            .map(Duration::from_secs)
            .unwrap_or(DEFAULT_TARGET_TIMEOUT)
    }

    pub fn get_expand(&self) -> Expand<'_> {
        self
            .common
            .get_expand()
            .target_options(&self.target_options)
            .coverage_dir(&self.coverage.local_path)
    }
}

pub struct CoverageTask {
    config: Config,
    poller: InputPoller<Message>,
}

impl CoverageTask {
    pub fn new(config: Config) -> Self {
        let poller = InputPoller::new("coverage");
        Self { config, poller }
    }

    pub async fn run(&mut self) -> Result<()> {
        info!("starting coverage task");

        if self.config.coverage_filter.is_some() {
            bail!("the `coverage_filter` option for the `coverage` task is deprecated");
        }

        self.config.coverage.init_pull().await?;

        let coverage_file = self.config.coverage.local_path.join(COVERAGE_FILE);

        let coverage = {
            if let Ok(text) = fs::read_to_string(&coverage_file).await {
                let json = BinaryCoverageJson::deserialize(&text)?;
                BinaryCoverage::try_from(json)?
            } else {
                BinaryCoverage::default()
            }
        };

        let allowlist = self.load_target_allowlist().await?;

        let heartbeat = self.config.common.init_heartbeat(None).await?;
        let job_result = self.config.common.init_job_result().await?;
        let mut seen_inputs = false;

        let target_exe_path =
            try_resolve_setup_relative_path(&self.config.common.setup_dir, &self.config.target_exe)
                .await?;
        let target_exe = target_exe_path
            .to_str()
            .ok_or_else(|| anyhow::format_err!("target_exe path is not valid unicode"))?;

        let mut context = TaskContext::new(
            &self.config,
            coverage,
            allowlist,
            heartbeat,
            job_result,
            target_exe.to_string(),
        )?;

        if !context.uses_input() {
            bail!("input is not specified on the command line or arguments for the target");
        }

        context.heartbeat.alive();

        info!("report initial coverage");
        context.report_coverage_stats().await;

        for dir in &self.config.readonly_inputs {
            debug!("recording coverage for {}", dir.local_path.display());

            dir.init_pull().await?;
            let dir_count = context.record_corpus(&dir.local_path).await?;

            if dir_count > 0 {
                seen_inputs = true;
            }

            info!(
                "recorded coverage for {} inputs from {}",
                dir_count,
                dir.local_path.display()
            );

            context.heartbeat.alive();
        }

        if seen_inputs {
            context.save_and_sync_coverage().await?;
        }

        context.heartbeat.alive();

        if let Some(queue) = &self.config.input_queue {
            info!("polling queue for new coverage inputs");

            let callback = CallbackImpl::new(queue.clone(), context)?;
            self.poller.run(callback).await?;
        }

        Ok(())
    }

    async fn load_target_allowlist(&self) -> Result<TargetAllowList> {
        // By default, all items are allowed.
        //
        // We will check for user allowlists for each item type. On Windows, we must ensure some
        // source files are excluded.
        let mut allowlist = TargetAllowList::default();

        if let Some(modules) = &self.config.module_allowlist {
            allowlist.modules = self.load_allowlist(modules).await?;
        }

        if let Some(source_files) = &self.config.source_allowlist {
            allowlist.source_files = self.load_allowlist(source_files).await?;
        }

        if cfg!(target_os = "windows") {
            // If on Windows, add a base denylist which excludes sanitizer-intercepted CRT and
            // process startup functions. Setting software breakpoints in these functions breaks
            // interceptor init, and causes test case execution to diverge.
            let interceptor_denylist = AllowList::parse(WINDOWS_INTERCEPTOR_DENYLIST)?;
            allowlist
                .source_files
                .extend_in_place(&interceptor_denylist);
        }

        Ok(allowlist)
    }

    async fn load_allowlist(&self, path: &str) -> Result<AllowList> {
        let resolved = try_resolve_setup_relative_path(&self.config.common.setup_dir, path).await?;
        let text = fs::read_to_string(&resolved).await?;
        AllowList::parse(&text)
    }
}

#[derive(Clone, Debug, Default)]
struct TargetAllowList {
    modules: AllowList,
    source_files: AllowList,
}

struct TaskContext<'a> {
    config: &'a Config,
    coverage: RwLock<BinaryCoverage>,
    module_allowlist: AllowList,
    source_allowlist: Arc<AllowList>,
    heartbeat: Option<TaskHeartbeatClient>,
    job_result: Option<TaskJobResultClient>,
    cache: Arc<DebugInfoCache>,
}

impl<'a> TaskContext<'a> {
    pub fn new(
        config: &'a Config,
        coverage: BinaryCoverage,
        allowlist: TargetAllowList,
        heartbeat: Option<TaskHeartbeatClient>,
        job_result: Option<TaskJobResultClient>,
        target_exe: String,
    ) -> Result<Self> {
        let cache = DebugInfoCache::new(allowlist.source_files.clone());
        let loader = Loader::new();

        // Preload the cache with the target executable, to avoid counting debuginfo analysis
        // time against the exeuction timeout for the first iteration.
        let module: Box<dyn Module> = LoadModule::load(&loader, FilePath::new(target_exe)?)?;

        cache
            .get_or_insert(&*module)
            .context("Failed to load debuginfo for target_exe when populating DebugInfoCache")?;

        Ok(Self {
            config,
            coverage: RwLock::new(coverage),
            module_allowlist: allowlist.modules,
            source_allowlist: Arc::new(allowlist.source_files),
            heartbeat,
            job_result,
            cache: Arc::new(cache),
        })
    }

    pub async fn record_input(&mut self, input: &Path) -> Result<()> {
        debug!("recording coverage for {}", input.display());
        let attempts = MAX_COVERAGE_RECORDING_ATTEMPTS;

        for attempt in 1..=attempts {
            let result = self.try_record_input(input).await;

            if let Err(err) = &result {
                // Recording failed, check if we can retry.
                if attempt < attempts {
                    // We will retry, but warn to capture the error if we succeed.
                    warn!(
                        "error recording coverage for input = {}: {:?}",
                        input.display(),
                        err
                    );
                } else {
                    // Final attempt, do not retry.
                    return result.with_context(|| {
                        format_err!(
                            "failed to record coverage for input = {} after {} attempts",
                            input.display(),
                            attempts
                        )
                    });
                }
            } else {
                // We successfully recorded the coverage for `input`, so stop.
                break;
            }
        }

        Ok(())
    }

    async fn try_record_input(&mut self, input: &Path) -> Result<()> {
        let coverage = self.record_impl(input).await?;
        let mut self_coverage = RwLock::write(&self.coverage).await;
        self_coverage.merge(&coverage);
        Ok(())
    }

    async fn record_impl(&mut self, input: &Path) -> Result<BinaryCoverage> {
        let module_allowlist = self.module_allowlist.clone();
        let cmd = self.command_for_input(input).await?;
        let timeout = self.config.timeout();
        let cache = self.cache.clone();
        let recorded = spawn_blocking(move || {
            CoverageRecorder::new(cmd)
                .debuginfo_cache(cache)
                .module_allowlist(module_allowlist)
                .timeout(timeout)
                .record()
        })
        .await??;

        if let Some(status) = recorded.output.status {
            if !status.success() {
                bail!("coverage recording failed, child status = {}", status);
            }
        }

        Ok(recorded.coverage)
    }

    fn uses_input(&self) -> bool {
        let input = PlaceHolder::Input.get_string();

        for entry in &self.config.target_options {
            if entry.contains(input) {
                return true;
            }
        }
        for (k, v) in &self.config.target_env {
            if k == input || v.contains(input) {
                return true;
            }
        }

        false
    }

    async fn command_for_input(&self, input: &Path) -> Result<Command> {
        let target_exe =
            try_resolve_setup_relative_path(&self.config.common.setup_dir, &self.config.target_exe)
                .await?;

        let expand = self
            .config
            .get_expand()
            .target_exe(&target_exe)
            .input_path(input);

        let mut cmd = Command::new(&target_exe);

        let target_options = expand.evaluate(&self.config.target_options)?;
        cmd.args(target_options);

        for (k, v) in &self.config.target_env {
            cmd.env(k, expand.evaluate_value(v)?);
        }

        // Make shared library resolution on Linux match behavior in other tasks.
        if cfg!(target_os = "linux") {
            let cmd_ld_library_path = cmd
                .get_envs()
                .find(|(k, _)| *k == LD_LIBRARY_PATH)
                .map(|(_, v)| v);

            // Depending on user-provided values, obtain a base value for `LD_LIBRARY_PATH`, which
            // we will update to include the local root of the setup directory.
            let ld_library_path = match cmd_ld_library_path {
                None => {
                    // The user did not provide an `LD_LIBRARY_PATH`, so the child process will
                    // inherit the current actual value (if any). It would be best to never inherit
                    // the current environment in any user subprocess invocation, but since we do,
                    // preserve the existing behavior.
                    std::env::var_os(LD_LIBRARY_PATH).unwrap_or_default()
                }
                Some(None) => {
                    // This is actually unreachable, since it can only occur as the result of a call
                    // to `env_clear(LD_LIBRARY_PATH)`. Even if this could happen, we'd reset it to
                    // the setup dir, so use the empty path as our base.
                    "".into()
                }
                Some(Some(path)) => {
                    // `LD_LIBRARY_PATH` was set by the user-provided `target_env`, and we may have
                    // expanded some placeholder variables. Extend that.
                    path.to_owned()
                }
            };

            // Add the setup directory to the library path and ensure it will occur in the child
            // environment.
            let ld_library_path =
                onefuzz::env::update_path(ld_library_path, &self.config.common.setup_dir)?;
            cmd.env(LD_LIBRARY_PATH, ld_library_path);
        }

        cmd.env_remove("RUST_LOG");
        cmd.stdin(Stdio::null());
        cmd.stdout(Stdio::piped());
        cmd.stderr(Stdio::piped());

        Ok(cmd)
    }

    pub async fn record_corpus(&mut self, dir: &Path) -> Result<usize> {
        use futures::stream::StreamExt;

        let mut corpus = fs::read_dir(dir)
            .await
            .map(ReadDirStream::new)
            .with_context(|| format!("unable to read corpus directory: {}", dir.display()))?;

        let mut count = 0;

        while let Some(entry) = corpus.next().await {
            match entry {
                Ok(entry) => {
                    if entry.file_type().await?.is_file() {
                        if let Err(e) = self.record_input(&entry.path()).await {
                            event!(coverage_failed; EventData::Path = entry.path().display().to_string());
                            metric!(coverage_failed; 1.0; EventData::Path = entry.path().display().to_string());
                            warn!(
                                "ignoring error recording coverage for input: {}, error: {}",
                                entry.path().display(),
                                e
                            );
                        } else {
                            count += 1;

                            // make sure we save & sync coverage every 10 inputs
                            if count % 10 == 0 {
                                self.save_and_sync_coverage().await?;
                            }
                        }
                    } else {
                        warn!("skipping non-file dir entry: {}", entry.path().display());
                    }
                }
                Err(err) => {
                    error!("{:?}", err);
                }
            }
        }

        Ok(count)
    }

    pub async fn report_coverage_stats(&self) {
        use EventData::*;

        let coverage = RwLock::read(&self.coverage).await;
        let s = CoverageStats::new(&coverage);
        event!(coverage_data; Covered = s.covered, Features = s.features, Rate = s.rate);
        metric!(coverage_data; 1.0; Covered = s.covered, Features = s.features, Rate = s.rate);
        self.job_result
            .send_direct(
                JobResultData::CoverageData,
                HashMap::from([
                    ("covered".to_string(), s.covered as f64),
                    ("features".to_string(), s.features as f64),
                    ("rate".to_string(), s.rate),
                ]),
            )
            .await;
    }

    pub async fn save_coverage(
        coverage: &RwLock<BinaryCoverage>,
        source_allowlist: &Arc<AllowList>,
        binary_coverage_path: &Path,
        source_coverage_path: &Path,
        copbertura_file_path: &Path,
    ) -> Result<()> {
        let source = Self::source_coverage(coverage, source_allowlist.clone()).await?;
        let coverage = coverage.read().await;

        Self::save_binary_coverage(&coverage, binary_coverage_path)?;
        Self::save_source_coverage(&source, source_coverage_path).await?;
        Self::save_cobertura_xml(&source, copbertura_file_path).await?;
        Ok(())
    }

    async fn source_coverage(
        coverage: &RwLock<BinaryCoverage>,
        source_allowlist: Arc<AllowList>,
    ) -> Result<SourceCoverage> {
        // Must be owned due to `spawn_blocking()` lifetimes.
        let allowlist = source_allowlist.clone();
        let binary = Arc::new(coverage.read().await.clone());
        // Conversion to source coverage heavy on blocking I/O.
        spawn_blocking(move || binary_to_source_coverage(&binary, &allowlist)).await?
    }

    pub async fn save_and_sync_coverage(&self) -> Result<()> {
        let copbertura_file_path = self
            .config
            .coverage
            .local_path
            .join(COBERTURA_COVERAGE_FILE);

        let source_coverage_path = self.config.coverage.local_path.join(SOURCE_COVERAGE_FILE);
        let binary_coverage_path = self.config.coverage.local_path.join(COVERAGE_FILE);

        Self::save_coverage(
            &self.coverage,
            &self.source_allowlist,
            &binary_coverage_path,
            &source_coverage_path,
            &copbertura_file_path,
        )
        .await?;
        self.config.coverage.sync_push().await?;
        Ok(())
    }

    async fn save_cobertura_xml(source: &SourceCoverage, path: &Path) -> Result<(), anyhow::Error> {
        let cobertura = CoberturaCoverage::from(source);
        let cobertura_coverage_file = std::fs::File::create(path)
            .with_context(|| format!("creating cobertura coverage file {}", path.display()))?;
        let cobertura_coverage_file_writer = std::io::BufWriter::new(cobertura_coverage_file);
        cobertura
            .write_xml(cobertura_coverage_file_writer)
            .with_context(|| format!("serializing cobertura coverage to {}", path.display()))?;
        Ok(())
    }

    async fn save_source_coverage(source: &SourceCoverage, path: &Path) -> Result<()> {
        let json = SourceCoverageJson::V1(SourceCoverageJsonV1::from(source));
        let source_coverage_file = std::fs::File::create(path)
            .with_context(|| format!("creating source coverage file {}", path.display()))?;
        let source_coverage_file_writer = std::io::BufWriter::new(source_coverage_file);
        serde_json::to_writer_pretty(source_coverage_file_writer, &json)
            .with_context(|| format!("serializing source coverage to {}", path.display()))?;
        Ok(())
    }

    fn save_binary_coverage(
        binary_coverage: &BinaryCoverage,
        path: &Path,
    ) -> Result<(), anyhow::Error> {
        let json = BinaryCoverageJson::V1(BinaryCoverageJsonV1::from(binary_coverage));

        let coverage_file = std::fs::File::create(path)
            .with_context(|| format!("creating coverage file {}", path.display()))?;
        let coverage_file_writer = std::io::BufWriter::new(coverage_file);
        serde_json::to_writer(coverage_file_writer, &json)
            .with_context(|| format!("serializing binary coverage to {}", path.display()))?;
        Ok(())
    }
}

#[async_trait]
impl<'a> Processor for TaskContext<'a> {
    async fn process(&mut self, _url: Option<Url>, input: &Path) -> Result<()> {
        self.heartbeat.alive();

        self.record_input(input).await?;
        self.report_coverage_stats().await;
        self.save_and_sync_coverage().await?;

        Ok(())
    }
}

#[derive(Default)]
struct CoverageStats {
    covered: u64,
    features: u64,
    rate: f64,
}

impl CoverageStats {
    pub fn new(coverage: &BinaryCoverage) -> Self {
        let mut stats = CoverageStats::default();

        for (_, module) in coverage.modules.iter() {
            for count in module.offsets.values() {
                stats.features += 1;

                if count.reached() {
                    stats.covered += 1;
                }
            }
        }

        if stats.features > 0 {
            stats.rate = (stats.covered as f64) / (stats.features as f64)
        }

        stats
    }
}

#[cfg(test)]
mod tests {
    use onefuzz::expand::PlaceHolder;
    use proptest::prelude::*;

    use crate::config_test_utils::GetExpandFields;

    use super::Config;

    impl GetExpandFields for Config {
        fn get_expand_fields(&self) -> Vec<(PlaceHolder, String)> {
            let mut params = self.common.get_expand_fields();
            params.push((PlaceHolder::TargetOptions, self.target_options.join(" ")));
            params.push((
                PlaceHolder::CoverageDir,
                dunce::canonicalize(&self.coverage.local_path)
                    .unwrap()
                    .to_string_lossy()
                    .to_string(),
            ));

            params
        }
    }

    config_test!(Config);
}
