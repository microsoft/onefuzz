// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::PathBuf;

use anyhow::Result;
use async_trait::async_trait;
use onefuzz::libfuzzer::LibFuzzer;

use crate::tasks::fuzz::libfuzzer::common;
use crate::tasks::utils::try_resolve_setup_relative_path;

/// Generic LibFuzzer with no special extra configuration.
///
/// Its configuration is fully controlled by the user, up to the constraints of the
/// `LibFuzzer` wrapper itself.
#[derive(Debug)]
pub struct GenericLibFuzzer;

#[async_trait]
impl common::LibFuzzerType for GenericLibFuzzer {
    type Config = ();

    async fn from_config(config: &common::Config<Self>) -> Result<LibFuzzer> {
        let target_exe =
            try_resolve_setup_relative_path(&config.common.setup_dir, &config.target_exe).await?;

        Ok(LibFuzzer::new(
            target_exe,
            config.target_options.clone(),
            config.target_env.clone(),
            config.common.setup_dir.clone(),
            config.common.extra_setup_dir.clone(),
            config
                .common
                .extra_output
                .as_ref()
                .map(|x| x.local_path.clone()),
            config.common.machine_identity.clone(),
        ))
    }

    async fn extra_setup(config: &common::Config<Self>) -> Result<()> {
        // this is needed on Windows, but we do it unconditionally
        let target_exe =
            try_resolve_setup_relative_path(&config.common.setup_dir, &config.target_exe).await?;

        // Set up a .local file on Windows before invoking the executable,
        // so that all DLLs are resolved to the exe’s folder in preference to the Windows/system DLLs.
        // The .local file is an empty file that tells DLL resolution to consider the same directory,
        // even for system (or KnownDLL) files.
        // See: https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-redirection#how-to-redirect-dlls-for-unpackaged-apps
        let dotlocal_file = add_dotlocal_extension(target_exe);
        if let Err(e) = tokio::fs::write(&dotlocal_file, &[]).await {
            // ignore already-exists error, report anything else
            if e.kind() != std::io::ErrorKind::AlreadyExists {
                return Err(anyhow::Error::from(e).context("creating .local file"));
            }
        }

        info!("Created .local file: {}", dotlocal_file.display());

        Ok(())
    }
}

fn add_dotlocal_extension(mut path: PathBuf) -> PathBuf {
    if let Some(ext) = path.extension() {
        let mut ext = ext.to_os_string();
        ext.push(".local");
        path.set_extension(ext);
    } else {
        path.set_extension("local");
    }

    path
}

pub type Config = common::Config<GenericLibFuzzer>;
pub type LibFuzzerFuzzTask = common::LibFuzzerFuzzTask<GenericLibFuzzer>;

#[cfg(test)]
mod test {
    use std::path::PathBuf;

    use super::add_dotlocal_extension;

    #[test]
    fn dotlocal_with_extension() {
        let path = PathBuf::from("executable.exe");
        assert_eq!(
            PathBuf::from("executable.exe.local"),
            add_dotlocal_extension(path)
        );
    }

    #[test]
    fn dotlocal_without_extension() {
        let path = PathBuf::from("executable");
        assert_eq!(
            PathBuf::from("executable.local"),
            add_dotlocal_extension(path)
        );
    }
}
