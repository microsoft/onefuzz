// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::{
    env,
    path::{Path, PathBuf},
    process::Stdio,
    sync::Arc,
};

use anyhow::{Context, Result};
use onefuzz::{fs::has_files, sha256::digest_file};
use tempfile::{tempdir, TempDir};
use tokio::{
    fs,
    process::{Child, Command},
};

use crate::tasks::coverage::libfuzzer_coverage::Config;

pub struct CoverageRecorder {
    config: Arc<Config>,
    script_path: PathBuf,
    // keep _temp_dir such that Drop cleans up temporary files
    _temp_dir: Option<TempDir>,
}

const SYMBOL_EXTRACT_ERROR: &str = "Target appears to be missing sancov instrumentation. This error can also happen if symbols for the target are not available.";

impl CoverageRecorder {
    pub async fn new(config: Arc<Config>) -> Result<Self> {
        let (script_path, _temp_dir) = match env::var("ONEFUZZ_TOOLS") {
            Ok(tools_dir) => {
                let script_path = PathBuf::from(tools_dir);
                if cfg!(target_os = "linux") {
                    (
                        script_path
                            .join("linux")
                            .join("libfuzzer-coverage")
                            .join("coverage_cmd.py"),
                        None,
                    )
                } else if cfg!(target_os = "windows") {
                    (
                        script_path
                            .join("win64")
                            .join("libfuzzer-coverage")
                            .join("DumpCounters.js"),
                        None,
                    )
                } else {
                    bail!("coverage recorder not implemented for target os");
                }
            }
            Err(_) => {
                let temp_dir = tempdir()?;
                let script_path = if cfg!(target_os = "linux") {
                    let script_path = temp_dir.path().join("coverage_cmd.py");
                    let content = include_bytes!(
                        "../../../../script/linux/libfuzzer-coverage/coverage_cmd.py"
                    );
                    fs::write(&script_path, content).await.with_context(|| {
                        format!("unable to write file: {}", script_path.display())
                    })?;
                    script_path
                } else if cfg!(target_os = "windows") {
                    let script_path = temp_dir.path().join("DumpCounters.js");
                    let content = include_bytes!(
                        "../../../../script/win64/libfuzzer-coverage/DumpCounters.js"
                    );
                    fs::write(&script_path, content).await.with_context(|| {
                        format!("unable to write file: {}", script_path.display())
                    })?;
                    script_path
                } else {
                    bail!("coverage recorder not implemented for target os");
                };

                (script_path, Some(temp_dir))
            }
        };

        Ok(Self {
            config,
            script_path,
            _temp_dir,
        })
    }

    /// Invoke a script to write coverage to a file.
    ///
    /// Per module coverage is written to:
    ///    coverage/inputs/<SHA256_OF_INPUT>/<module_name>.cov
    ///
    /// The `.cov` file is a binary dump of the 8-bit PC counter table.
    pub async fn record(&mut self, test_input: impl AsRef<Path>) -> Result<PathBuf> {
        let test_input = test_input.as_ref();

        let coverage_path = {
            let digest = digest_file(test_input).await?;
            self.config.coverage.local_path.join("inputs").join(digest)
        };

        fs::create_dir_all(&coverage_path).await.with_context(|| {
            format!(
                "unable to create coverage path: {}",
                coverage_path.display()
            )
        })?;

        let script = self.invoke_debugger_script(test_input, &coverage_path)?;
        let output = script.wait_with_output().await?;

        let stdout = String::from_utf8_lossy(&output.stdout);
        let stderr = String::from_utf8_lossy(&output.stderr);

        if !output.status.success() {
            let err = format_err!("coverage recording failed: {}", output.status);
            error!("{}", err);
            error!("recording stderr: {}", stderr);
            error!("recording stdout: {}", stdout);

            return Err(err);
        } else {
            debug!("recording stderr: {}", stderr);
            debug!("recording stdout: {}", stdout);
        }

        if !has_files(&coverage_path).await? {
            tokio::fs::remove_dir(&coverage_path)
                .await
                .with_context(|| {
                    format!(
                        "unable to remove coverage path: {}",
                        coverage_path.display()
                    )
                })?;

            let filename = test_input
                .file_name()
                .ok_or_else(|| format_err!("unable to identify coverage input filename"))?;

            bail!(
                "{}\ntarget_exe: {}\ninput: {:?}\ndebugger stdout: {}\ndebugger stderr: {}",
                SYMBOL_EXTRACT_ERROR,
                self.config.target_exe.display(),
                filename,
                stdout,
                stderr
            );
        }

        Ok(coverage_path)
    }

    #[cfg(target_os = "linux")]
    fn invoke_debugger_script(&self, test_input: &Path, output: &Path) -> Result<Child> {
        let mut cmd = Command::new("gdb");
        cmd.arg(&self.config.target_exe)
            .arg("-nh")
            .arg("-batch")
            .arg("-x")
            .arg(&self.script_path)
            .arg("-ex")
            .arg(format!(
                "coverage {} {} {}",
                &self.config.target_exe.to_string_lossy(),
                test_input.to_string_lossy(),
                output.to_string_lossy(),
            ))
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .kill_on_drop(true);

        for (k, v) in &self.config.target_env {
            cmd.env(k, v);
        }

        let child = cmd.spawn().context("gdb failed to start")?;

        Ok(child)
    }

    #[cfg(target_os = "windows")]
    fn invoke_debugger_script(&self, test_input: &Path, output: &Path) -> Result<Child> {
        let should_disable_sympath = !self.config.target_env.contains_key("_NT_SYMBOL_PATH");

        let cdb_cmd = format!(
            ".scriptload {}; !dumpcounters {:?}, {}; q",
            self.script_path.to_string_lossy(),
            output.to_string_lossy(),
            should_disable_sympath,
        );

        let mut cmd = Command::new("cdb.exe");

        cmd.arg("-c")
            .arg(cdb_cmd)
            .arg(&self.config.target_exe)
            .arg(test_input)
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .kill_on_drop(true);

        for (k, v) in &self.config.target_env {
            cmd.env(k, v);
        }

        let child = cmd.spawn().context("cdb.exe failed to start")?;

        Ok(child)
    }
}
