// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::{
    env,
    path::{Path, PathBuf},
    process::Stdio,
    sync::Arc,
};

use anyhow::Result;
use onefuzz::{
    fs::{has_files, OwnedDir},
    sha256::digest_file,
};
use tokio::{
    fs,
    process::{Child, Command},
};

use crate::tasks::coverage::libfuzzer_coverage::Config;

pub struct CoverageRecorder {
    config: Arc<Config>,
    script_dir: OwnedDir,
}

impl CoverageRecorder {
    pub fn new(config: Arc<Config>) -> Self {
        let script_dir =
            OwnedDir::new(env::var("ONEFUZZ_TOOLS").unwrap_or_else(|_| "script".to_string()));

        Self { config, script_dir }
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
            self.config.coverage.path.join("inputs").join(digest)
        };

        fs::create_dir_all(&coverage_path).await?;

        let script = self.invoke_debugger_script(test_input, &coverage_path)?;
        let output = script.wait_with_output().await?;

        if !output.status.success() {
            let err = format_err!("coverage recording failed: {}", output.status);
            error!("{}", err);
            error!(
                "recording stderr: {}",
                String::from_utf8_lossy(&output.stderr)
            );
            error!(
                "recording stdout: {}",
                String::from_utf8_lossy(&output.stdout)
            );

            return Err(err);
        } else {
            verbose!(
                "recording stderr: {}",
                String::from_utf8_lossy(&output.stderr)
            );
            verbose!(
                "recording stdout: {}",
                String::from_utf8_lossy(&output.stdout)
            );
        }

        if !has_files(&coverage_path).await? {
            tokio::fs::remove_dir(&coverage_path).await?;
            bail!("no coverage files for input: {}", test_input.display());
        }

        Ok(coverage_path)
    }

    #[cfg(target_os = "linux")]
    fn invoke_debugger_script(&self, test_input: &Path, output: &Path) -> Result<Child> {
        let script_path = self
            .script_dir
            .path()
            .join("linux")
            .join("libfuzzer-coverage")
            .join("coverage_cmd.py");

        let mut cmd = Command::new("gdb");
        cmd.arg(&self.config.target_exe)
            .arg("-nh")
            .arg("-batch")
            .arg("-x")
            .arg(script_path)
            .arg("-ex")
            .arg(format!(
                "coverage {} {} {}",
                &self.config.target_exe.to_string_lossy(),
                test_input.to_string_lossy(),
                output.to_string_lossy(),
            ))
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .kill_on_drop(true);

        for (k, v) in &self.config.target_env {
            cmd.env(k, v);
        }

        let child = cmd.spawn()?;

        Ok(child)
    }

    #[cfg(target_os = "windows")]
    fn invoke_debugger_script(&self, test_input: &Path, output: &Path) -> Result<Child> {
        let script_path = self
            .script_dir
            .path()
            .join("win64")
            .join("libfuzzer-coverage")
            .join("DumpCounters.js");

        let cdb_cmd = format!(
            ".scriptload {}; !dumpcounters {:?}; q",
            script_path.to_string_lossy(),
            output.to_string_lossy()
        );

        let mut cmd = Command::new("cdb.exe");

        cmd.arg("-c")
            .arg(cdb_cmd)
            .arg(&self.config.target_exe)
            .arg(test_input)
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .kill_on_drop(true);

        for (k, v) in &self.config.target_env {
            cmd.env(k, v);
        }

        let child = cmd.spawn()?;

        Ok(child)
    }
}
