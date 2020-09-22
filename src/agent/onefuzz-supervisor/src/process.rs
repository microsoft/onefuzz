/// Serializable representation of a process output.
#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct Output {
    pub exit_status: ExitStatus,
    pub stderr: String,
    pub stdout: String,
}

impl From<std::process::Output> for Output {
    fn from(output: std::process::Output) -> Self {
        let exit_status = output.status.into();
        let stderr = String::from_utf8_lossy(&output.stderr).to_string();
        let stdout = String::from_utf8_lossy(&output.stdout).to_string();

        Self {
            exit_status,
            stderr,
            stdout,
        }
    }
}

/// Serializable representation of a process exit status.
#[derive(Clone, Copy, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct ExitStatus {
    pub code: Option<i32>,
    pub signal: Option<i32>,
    pub success: bool,
}

impl From<std::process::ExitStatus> for ExitStatus {
    #[cfg(target_os = "windows")]
    fn from(status: std::process::ExitStatus) -> Self {
        Self {
            code: status.code(),
            signal: None,
            success: status.success(),
        }
    }

    #[cfg(target_os = "linux")]
    fn from(status: std::process::ExitStatus) -> Self {
        use std::os::unix::process::ExitStatusExt;

        Self {
            code: status.code(),
            signal: status.signal(),
            success: status.success(),
        }
    }
}
