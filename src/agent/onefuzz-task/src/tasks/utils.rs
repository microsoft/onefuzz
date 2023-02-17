// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{Context, Result};
use async_trait::async_trait;
use onefuzz::{http::ResponseExt, jitter::delay_with_jitter};
use reqwest::{Client, Url};
use reqwest_retry::SendRetry;
use std::path::{Path, PathBuf};
use std::time::Duration;
use tokio::{fs, io};

pub async fn download_input(input_url: Url, dst: impl AsRef<Path>) -> Result<PathBuf> {
    let file_name = input_url.path_segments().unwrap().last().unwrap();
    let file_path = dst.as_ref().join(file_name);

    if input_url.scheme().to_lowercase() == "file" {
        let input_file_path = input_url
            .to_file_path()
            .map_err(|_| anyhow!("Invalid file Url"))?;
        fs::copy(&input_file_path, &file_path).await?;
    } else {
        let resp = Client::new()
            .get(input_url)
            .send_retry_default()
            .await
            .context("download_input")?
            .error_for_status_with_body()
            .await
            .context("download_input status body")?;

        let body = resp.bytes().await?;
        let mut body = body.as_ref();

        let file = fs::OpenOptions::new()
            .create(true)
            .write(true)
            .open(&file_path)
            .await?;
        let mut writer = io::BufWriter::new(file);

        io::copy(&mut body, &mut writer).await?;
    }
    Ok(file_path)
}

pub async fn reset_tmp_dir(tmp_dir: impl AsRef<Path>) -> Result<()> {
    let tmp_dir = tmp_dir.as_ref();

    let dir_exists = fs::metadata(tmp_dir).await.is_ok();

    if dir_exists {
        fs::remove_dir_all(tmp_dir).await?;

        debug!("deleted {}", tmp_dir.display());
    }

    fs::create_dir_all(tmp_dir).await?;

    debug!("created {}", tmp_dir.display());

    Ok(())
}

pub fn parse_url_data(data: &[u8]) -> Result<Url> {
    let text = std::str::from_utf8(data)?;
    let url = Url::parse(text)?;

    Ok(url)
}

#[async_trait]
pub trait CheckNotify {
    async fn is_notified(&self, delay: Duration) -> bool;
}

#[async_trait]
impl CheckNotify for tokio::sync::Notify {
    async fn is_notified(&self, delay: Duration) -> bool {
        let notify = self;
        tokio::select! {
            () = delay_with_jitter(delay) => false,
            () = notify.notified() => true,
        }
    }
}

pub fn parse_key_value(value: &str) -> Result<(String, String)> {
    let offset = value
        .find('=')
        .ok_or_else(|| format_err!("invalid key=value, no = found {:?}", value))?;

    Ok((value[..offset].to_string(), value[offset + 1..].to_string()))
}

pub fn default_bool_true() -> bool {
    true
}

/// Try to resolve an ambiguous setup-relative subpath, returning an error if not found.
pub async fn try_resolve_setup_relative_path(
    setup_dir: impl AsRef<Path>,
    subpath: impl AsRef<Path>,
) -> Result<PathBuf> {
    let setup_dir = setup_dir.as_ref();
    let subpath = subpath.as_ref();

    resolve_setup_relative_path(setup_dir, subpath)
        .await?
        .ok_or_else(|| {
            anyhow::format_err!(
                "unable to resolve subpath `{}` under setup dir `{}`",
                subpath.display(),
                setup_dir.display()
            )
        })
}

/// Try to resolve an ambiguous setup-relative subpath, returning `None` if not found.
pub async fn resolve_setup_relative_path(
    setup_dir: impl AsRef<Path>,
    subpath: impl AsRef<Path>,
) -> Result<Option<PathBuf>> {
    let setup_dir = setup_dir.as_ref();
    let subpath = subpath.as_ref();

    // Case: non-legacy `subpath`, relativized to `setup_dir`.
    //
    // Even if `subpath` is prefixed by `setup`, it is because it truly names a file in a
    // non-root subdirectory like `{setup_dir}/setup`.
    {
        let resolved = setup_dir.join(subpath);
        if exists(&resolved).await {
            return Ok(Some(resolved));
        }
    }

    // Case: non-legacy `subpath` that uses the `{setup_dir}` placeholder variable.
    //
    // Note that we do not do full expansion, just a form of restricted find/replace.
    {
        let subpath = subpath.strip_prefix("{setup_dir}").unwrap_or(subpath);
        let resolved = setup_dir.join(subpath);
        if exists(&resolved).await {
            return Ok(Some(resolved));
        }
    }

    // Case: legacy `subpath`.
    //
    // The `setup_dir`-relativized `subpath` was prefixed server-side with the hardcoded
    // relative path `setup`. We only expect to see this after upgrading deployments that
    // have pending tasks with legacy configs.
    {
        let subpath = subpath.strip_prefix("setup").unwrap_or(subpath);
        let resolved = setup_dir.join(subpath);
        if exists(&resolved).await {
            return Ok(Some(resolved));
        }
    }

    Ok(None)
}

async fn exists(path: impl AsRef<Path>) -> bool {
    fs::metadata(path).await.is_ok()
}

#[cfg(test)]
mod tests {
    use std::path::Path;

    use anyhow::Result;
    use tempfile::TempDir;

    use super::*;

    fn init_setup_dir(actual: impl AsRef<Path>) -> Result<TempDir> {
        let actual = actual.as_ref();

        let setup_dir = TempDir::new()?;

        // If the actual file is nested in any subdirectories of `setup`, ensure that the
        // intermediates exist.
        if let Some(parent) = actual.parent() {
            let intermediate = setup_dir.path().join(parent);
            std::fs::create_dir_all(intermediate)?;
        }

        // Create an (empty) file on-disk for the file being referenced.
        std::fs::write(setup_dir.path().join(actual), "")?;

        Ok(setup_dir)
    }

    async fn test_case(relative: impl AsRef<Path>, given: impl AsRef<Path>) -> Result<()> {
        let relative = relative.as_ref();

        let setup_dir = init_setup_dir(relative)?;

        let expected = setup_dir.path().join(relative);
        let resolved = resolve_setup_relative_path(setup_dir.path(), given).await?;

        assert_eq!(Some(expected), resolved);

        Ok(())
    }

    #[tokio::test]
    async fn test_resolve_setup_relative_path_root() -> Result<()> {
        const RELATIVE: &str = "fuzz.exe";

        test_case(RELATIVE, "fuzz.exe").await?;
        test_case(RELATIVE, "setup/fuzz.exe").await?;
        test_case(RELATIVE, "{setup_dir}/fuzz.exe").await?;

        Ok(())
    }

    #[tokio::test]
    async fn test_resolve_setup_relative_path_nested() -> Result<()> {
        const RELATIVE: &str = "x/fuzz.exe";

        test_case(RELATIVE, "x/fuzz.exe").await?;
        test_case(RELATIVE, "setup/x/fuzz.exe").await?;
        test_case(RELATIVE, "{setup_dir}/x/fuzz.exe").await?;

        Ok(())
    }

    #[tokio::test]
    async fn test_resolve_setup_relative_path_double_nested() -> Result<()> {
        const RELATIVE: &str = "x/y/fuzz.exe";

        test_case(RELATIVE, "x/y/fuzz.exe").await?;
        test_case(RELATIVE, "setup/x/y/fuzz.exe").await?;
        test_case(RELATIVE, "{setup_dir}/x/y/fuzz.exe").await?;

        Ok(())
    }

    #[tokio::test]
    async fn test_resolve_setup_relative_path_nested_setup() -> Result<()> {
        const RELATIVE: &str = "setup/fuzz.exe";

        test_case(RELATIVE, "setup/fuzz.exe").await?;
        test_case(RELATIVE, "setup/setup/fuzz.exe").await?;
        test_case(RELATIVE, "{setup_dir}/setup/fuzz.exe").await?;

        Ok(())
    }
}
