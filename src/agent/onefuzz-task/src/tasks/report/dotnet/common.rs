// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::ffi::OsStr;
use std::path::{Path, PathBuf};
use std::process::{Output, Stdio};

use anyhow::Result;
use tokio::fs;
use tokio::process::Command;
use tokio::task::spawn_blocking;

pub async fn collect_exception_info(
    args: &[impl AsRef<OsStr>],
    env: impl IntoIterator<Item = (impl AsRef<OsStr>, impl AsRef<OsStr>)>,
) -> Result<Option<DotnetExceptionInfo>> {
    // Create temp dir cooperatively.
    let tmp_dir = spawn_blocking(tempfile::tempdir).await??;

    let dump_path = tmp_dir.path().join(DUMP_FILE_NAME);

    let dump = match collect_dump(args, env, &dump_path).await? {
        Some(dump) => dump,
        None => {
            warn!("no minidump found, expected at {}", dump_path.display());
            return Ok(None);
        }
    };

    let exception = dump.exception().await;

    // Remove temp dir cooperatively.
    spawn_blocking(move || tmp_dir).await?;

    match exception {
        Ok(r) => Ok(Some(r)),
        Err(e) => {
            error!("unable to extract exception info: {}", e);
            Ok(None)
        }
    }
}

const DUMP_FILE_NAME: &str = "tmp.dmp";

// See: https://docs.microsoft.com/en-us/dotnet/core/diagnostics/dumps
const ENABLE_MINIDUMP_VAR: &str = "COMPlus_DbgEnableMiniDump";
const MINIDUMP_TYPE_VAR: &str = "COMPlus_DbgMiniDumpType";
const MINIDUMP_NAME_VAR: &str = "COMPlus_DbgMiniDumpName";

const MINIDUMP_ENABLE: &str = "1";
const MINIDUMP_TYPE_FULL: &str = "4";

// Invoke target with .NET runtime environment vars set to create minidumps.
//
// Returns the newly-created dump file, when present.
async fn collect_dump(
    args: impl IntoIterator<Item = impl AsRef<OsStr>>,
    env: impl IntoIterator<Item = (impl AsRef<OsStr>, impl AsRef<OsStr>)>,
    dump_path: impl AsRef<Path>,
) -> Result<Option<DotnetDumpFile>> {
    let dump_path = dump_path.as_ref();

    let dotnet = dotnet_path()?;
    let mut cmd = Command::new(dotnet);
    cmd.arg("exec");
    cmd.args(args);

    cmd.envs(env);

    // Set `dotnet` environment vars to enable saving minidumps on crash.
    cmd.env(ENABLE_MINIDUMP_VAR, MINIDUMP_ENABLE);
    cmd.env(MINIDUMP_TYPE_VAR, MINIDUMP_TYPE_FULL);
    cmd.env(MINIDUMP_NAME_VAR, dump_path);

    let mut child = cmd.spawn()?;
    let exit_status = child.wait().await?;

    if exit_status.success() {
        warn!("dotnet target exited normally when attempting to collect minidump");
    }

    let metadata = fs::metadata(dump_path).await;

    if metadata.is_ok() {
        // File exists and is readable if metadata is.
        let dump = DotnetDumpFile::new(dump_path.to_owned());

        Ok(Some(dump))
    } else {
        warn!("target exited nonzero, but no dump file found");

        Ok(None)
    }
}

pub struct DotnetDumpFile {
    path: PathBuf,
}

impl DotnetDumpFile {
    pub fn new(path: PathBuf) -> Self {
        Self { path }
    }

    pub async fn exception(&self) -> Result<DotnetExceptionInfo> {
        let output = self.exec_sos_command(SOS_PRINT_EXCEPTION).await?;
        let text = String::from_utf8_lossy(&output.stdout);
        parse_sos_print_exception_output(&text)
    }

    async fn exec_sos_command(&self, sos_cmd: &str) -> Result<Output> {
        let dotnet_dump = dotnet_dump_path()?;
        let mut cmd = Command::new(&dotnet_dump);

        // Run `dotnet-dump analyze` with a single SOS command on startup, then
        // exit the otherwise-interactive SOS session.
        let dump_path = self.path.display().to_string();
        let args = ["analyze", &dump_path, "-c", sos_cmd, "-c", SOS_EXIT];
        cmd.args(args);

        cmd.stderr(Stdio::piped());
        cmd.stdout(Stdio::piped());

        let output = cmd.spawn()?.wait_with_output().await?;

        if !output.status.success() {
            bail!(
                "SOS session for command `{}` exited with error {}: {}",
                sos_cmd,
                output.status,
                String::from_utf8_lossy(&output.stdout),
            );
        }

        Ok(output)
    }
}

#[derive(Debug, Eq, PartialEq)]
pub struct DotnetExceptionInfo {
    pub exception: String,
    pub message: String,
    pub inner_exception: Option<String>,
    pub call_stack: Vec<String>,
    pub hresult: String,
}

pub fn parse_sos_print_exception_output(text: &str) -> Result<DotnetExceptionInfo> {
    use std::io::*;

    use regex::Regex;

    lazy_static::lazy_static! {
        pub static ref EXCEPTION_TYPE: Regex = Regex::new(r#"^Exception type:\s+(.+)$"#).unwrap();
        pub static ref EXCEPTION_MESSAGE: Regex = Regex::new(r#"^Message:\s+(.*)$"#).unwrap();
        pub static ref INNER_EXCEPTION: Regex = Regex::new(r#"^InnerException:\s+(.*)$"#).unwrap();
        pub static ref STACK_FRAME: Regex = Regex::new(r#"^\s*([[:xdigit:]]+) ([[:xdigit:]]+) (.+)$"#).unwrap();
        pub static ref HRESULT: Regex = Regex::new(r#"^HResult:\s+([[:xdigit:]]+)$"#).unwrap();
    }

    let reader = BufReader::new(text.as_bytes());

    let mut exception: Option<String> = None;
    let mut message: Option<String> = None;
    let mut inner_exception: Option<String> = None;
    let mut call_stack: Vec<String> = vec![];
    let mut hresult: Option<String> = None;

    for line in reader.lines() {
        let line = match &line {
            Ok(line) => line,
            Err(err) => {
                warn!("error parsing line: {}", err);
                continue;
            }
        };

        if let Some(captures) = EXCEPTION_TYPE.captures(line) {
            if let Some(c) = captures.get(1) {
                exception = Some(c.as_str().to_owned());
                continue;
            }
        }

        if let Some(captures) = EXCEPTION_MESSAGE.captures(line) {
            if let Some(c) = captures.get(1) {
                message = Some(c.as_str().to_owned());
                continue;
            }
        }

        if let Some(captures) = INNER_EXCEPTION.captures(line) {
            if let Some(c) = captures.get(1) {
                inner_exception = Some(c.as_str().to_owned());
                continue;
            }
        }

        if let Some(captures) = STACK_FRAME.captures(line) {
            if let Some(c) = captures.get(3) {
                let frame = c.as_str().to_owned();
                call_stack.push(frame);
                continue;
            }
        }

        if let Some(captures) = HRESULT.captures(line) {
            if let Some(c) = captures.get(1) {
                hresult = Some(c.as_str().to_owned());
                continue;
            }
        }
    }

    let exception =
        exception.ok_or_else(|| format_err!("missing exception type, output was:\n{}", text))?;
    let message = message.ok_or_else(|| format_err!("missing exception message"))?;

    let inner_exception = inner_exception.ok_or_else(|| format_err!("missing inner exception"))?;
    let inner_exception = if inner_exception == "<none>" {
        None
    } else {
        Some(inner_exception)
    };

    let hresult = hresult.ok_or_else(|| format_err!("missing exception hresult"))?;

    if call_stack.is_empty() {
        bail!("missing call_stack");
    }

    Ok(DotnetExceptionInfo {
        exception,
        message,
        inner_exception,
        call_stack,
        hresult,
    })
}

// https://docs.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-dump#analyze-sos-commands
const SOS_EXIT: &str = "exit";
const SOS_PRINT_EXCEPTION: &str = "printexception -lines";

fn dotnet_path() -> Result<PathBuf> {
    let dotnet_root_dir = std::env::var("DOTNET_ROOT")?;

    #[cfg(target_os = "windows")]
    let exe_name = "dotnet.exe";

    #[cfg(not(target_os = "windows"))]
    let exe_name = "dotnet";

    let exe_path = Path::new(&dotnet_root_dir).join(exe_name);

    Ok(exe_path)
}

fn dotnet_dump_path() -> Result<PathBuf> {
    let tools_dir = std::env::var("ONEFUZZ_TOOLS")?;

    #[cfg(target_os = "windows")]
    let exe_name = "dotnet-dump.exe";

    #[cfg(not(target_os = "windows"))]
    let exe_name = "dotnet-dump";

    let exe_path = Path::new(&tools_dir).join(exe_name);

    Ok(exe_path)
}

#[cfg(test)]
mod tests {
    use super::{parse_sos_print_exception_output, DotnetExceptionInfo};
    use pretty_assertions::assert_eq;

    #[test]
    fn parse_works_as_expected() {
        let text = r#"Exception object: 000001d9cba32740
Exception type:   System.IndexOutOfRangeException
Message:          Index was outside the bounds of the array.
InnerException:   <none>
StackTrace (generated):
    SP               IP               Function
    0000000674D7B860 00007FFD85A660F6 System.Private.CoreLib!System.ThrowHelper.ThrowIndexOutOfRangeException()+0x36 [/_/src/libraries/System.Private.CoreLib/src/System/ThrowHelper.cs @ 68]
    0000000674D7B8A0 00007FFDE4D23B59 System.Private.CoreLib!System.ReadOnlySpan`1[[System.Byte, System.Private.CoreLib]].get_Item(Int32)+0x19 [/_/src/libraries/System.Private.CoreLib/src/System/ReadOnlySpan.cs @ 148]
    0000000674D7B8D0 00007FFD85A660A5 GoodBad!GoodBad.BinaryParser.ProcessInput(System.ReadOnlySpan`1<Byte>)+0xb5 [/home/runner/work/onefuzz/onefuzz/src/integration-tests/GoodBad/GoodBad.cs @ 22]
    0000000674D7B900 00007FFD85A65F81 GoodBad!GoodBad.Fuzzer.TestInput(System.ReadOnlySpan`1<Byte>)+0x61 [/home/runner/work/onefuzz/onefuzz/src/integration-tests/GoodBad/GoodBad.cs @ 31]
    0000000674D7B950 00007FFD85A65AB2 SharpFuzz!SharpFuzz.Fuzzer+LibFuzzer.RunWithoutLibFuzzer(SharpFuzz.ReadOnlySpanAction)+0x152
    0000000674D7B9F0 00007FFD85A656B2 SharpFuzz!SharpFuzz.Fuzzer+LibFuzzer.Run(SharpFuzz.ReadOnlySpanAction)+0x2b2
    0000000674D7E6D0 00007FFD85A652FC LibFuzzerDotnetLoader!LibFuzzerDotnetLoader.Program.TryTestOne[[System.__Canon, System.Private.CoreLib]](System.Reflection.MethodInfo, System.Func`2<System.__Canon,SharpFuzz.ReadOnlySpanAction>)+0x29c [D:\a\onefuzz\onefuzz\src\agent\LibFuzzerDotnetLoader\Program.cs @ 117]
    0000000674D7E7E0 00007FFD85A64F99 LibFuzzerDotnetLoader!LibFuzzerDotnetLoader.Program.TryTestOneSpan(System.Reflection.MethodInfo)+0xf9 [D:\a\onefuzz\onefuzz\src\agent\LibFuzzerDotnetLoader\Program.cs @ 123]
    0000000674D7E840 00007FFD85A61122 LibFuzzerDotnetLoader!LibFuzzerDotnetLoader.Program.TryMain()+0x2b2 [D:\a\onefuzz\onefuzz\src\agent\LibFuzzerDotnetLoader\Program.cs @ 82]
    0000000674D7E9D0 00007FFD85A44016 LibFuzzerDotnetLoader!LibFuzzerDotnetLoader.Program.Main(System.String[])+0x46 [D:\a\onefuzz\onefuzz\src\agent\LibFuzzerDotnetLoader\Program.cs @ 57]

StackTraceString: <none>
HResult: 80131508
There are nested exceptions on this thread. Run with -nested for details"#;

        let result = parse_sos_print_exception_output(text);
        let expected = DotnetExceptionInfo {
            exception: "System.IndexOutOfRangeException".to_string(),
            message: "Index was outside the bounds of the array.".to_string(),
            inner_exception: None,
            call_stack: [
                "System.Private.CoreLib!System.ThrowHelper.ThrowIndexOutOfRangeException()+0x36 [/_/src/libraries/System.Private.CoreLib/src/System/ThrowHelper.cs @ 68]",
                "System.Private.CoreLib!System.ReadOnlySpan`1[[System.Byte, System.Private.CoreLib]].get_Item(Int32)+0x19 [/_/src/libraries/System.Private.CoreLib/src/System/ReadOnlySpan.cs @ 148]",
                "GoodBad!GoodBad.BinaryParser.ProcessInput(System.ReadOnlySpan`1<Byte>)+0xb5 [/home/runner/work/onefuzz/onefuzz/src/integration-tests/GoodBad/GoodBad.cs @ 22]",
                "GoodBad!GoodBad.Fuzzer.TestInput(System.ReadOnlySpan`1<Byte>)+0x61 [/home/runner/work/onefuzz/onefuzz/src/integration-tests/GoodBad/GoodBad.cs @ 31]",
                "SharpFuzz!SharpFuzz.Fuzzer+LibFuzzer.RunWithoutLibFuzzer(SharpFuzz.ReadOnlySpanAction)+0x152",
                "SharpFuzz!SharpFuzz.Fuzzer+LibFuzzer.Run(SharpFuzz.ReadOnlySpanAction)+0x2b2",
                "LibFuzzerDotnetLoader!LibFuzzerDotnetLoader.Program.TryTestOne[[System.__Canon, System.Private.CoreLib]](System.Reflection.MethodInfo, System.Func`2<System.__Canon,SharpFuzz.ReadOnlySpanAction>)+0x29c [D:\\a\\onefuzz\\onefuzz\\src\\agent\\LibFuzzerDotnetLoader\\Program.cs @ 117]",
                "LibFuzzerDotnetLoader!LibFuzzerDotnetLoader.Program.TryTestOneSpan(System.Reflection.MethodInfo)+0xf9 [D:\\a\\onefuzz\\onefuzz\\src\\agent\\LibFuzzerDotnetLoader\\Program.cs @ 123]",
                "LibFuzzerDotnetLoader!LibFuzzerDotnetLoader.Program.TryMain()+0x2b2 [D:\\a\\onefuzz\\onefuzz\\src\\agent\\LibFuzzerDotnetLoader\\Program.cs @ 82]",
                "LibFuzzerDotnetLoader!LibFuzzerDotnetLoader.Program.Main(System.String[])+0x46 [D:\\a\\onefuzz\\onefuzz\\src\\agent\\LibFuzzerDotnetLoader\\Program.cs @ 57]",
            ].iter().map(|x| x.to_string()).collect(),
            hresult: "80131508".to_string(),
        };

        assert!(result.is_ok());
        assert_eq!(result.unwrap(), expected);
    }
}
