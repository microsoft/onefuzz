// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{Context, Result};
use futures::stream::StreamExt;
use std::path::{Path, PathBuf};
#[cfg(target_os = "linux")]
use std::process::Stdio;
use tokio::fs;
#[cfg(target_os = "linux")]
use tokio::process::Command;

const ONEFUZZ_ROOT_ENV: &str = "ONEFUZZ_ROOT";

pub fn onefuzz_root() -> Result<PathBuf> {
    let path = match std::env::var_os(ONEFUZZ_ROOT_ENV) {
        Some(path) => PathBuf::from(path),
        None => std::env::current_dir()?,
    };
    Ok(path)
}

pub fn onefuzz_etc() -> Result<PathBuf> {
    Ok(onefuzz_root()?.join("etc"))
}

pub async fn has_files(path: impl AsRef<Path>) -> Result<bool> {
    let path = path.as_ref();
    let mut paths = fs::read_dir(&path)
        .await
        .with_context(|| format!("unable to check if directory has files: {}", path.display()))?;
    let result = paths.next_entry().await?.is_some();
    Ok(result)
}

pub async fn list_files(path: impl AsRef<Path>) -> Result<Vec<PathBuf>> {
    let path = path.as_ref();
    let paths = fs::read_dir(&path)
        .await
        .with_context(|| format!("unable to list files: {}", path.display()))?;

    let mut files = paths
        .filter_map(|x| async {
            match x {
                Ok(x) => {
                    if match x.metadata().await {
                        Ok(x) => x.is_file(),
                        Err(_) => false,
                    } {
                        Some(x.path())
                    } else {
                        None
                    }
                }
                Err(_) => None,
            }
        })
        .collect::<Vec<_>>()
        .await;

    files.sort();

    Ok(files)
}

#[cfg(target_os = "linux")]
pub async fn set_executable(path: impl AsRef<Path>) -> Result<()> {
    let path = path.as_ref();

    let output = Command::new("chmod")
        .arg("-R")
        .arg("+x")
        .arg(path)
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::piped())
        .spawn()
        .with_context(|| format!("command failed to start: chmod -R +x {}", path.display()))?
        .wait_with_output()
        .await
        .with_context(|| format!("command failed to run: chmod -R +x {}", path.display()))?;

    if !output.status.success() {
        bail!("'chmod -R +x' of {:?} failed: {}", path, output.status);
    } else {
        Ok(())
    }
}

#[cfg(target_os = "windows")]
pub async fn set_executable(_path: impl AsRef<Path>) -> Result<()> {
    Ok(())
}

pub async fn exists(entry: impl AsRef<Path>) -> Result<bool> {
    use tokio::io::ErrorKind::NotFound;

    let metadata = fs::metadata(entry).await;

    if let Err(err) = &metadata {
        if err.kind() == NotFound {
            return Ok(false);
        }
    }

    // Return an error if it was anything other than `NotFound`.
    metadata?;

    Ok(true)
}

pub async fn write_file(path: impl AsRef<Path>, content: &str) -> Result<()> {
    let path = path.as_ref();
    let parent = path
        .parent()
        .ok_or_else(|| format_err!("no parent for: {}", path.display()))?;
    fs::create_dir_all(parent)
        .await
        .with_context(|| format!("unable to create nested path: {}", parent.display()))?;
    fs::write(path, content)
        .await
        .with_context(|| format!("unable to write file: {}", path.display()))?;
    Ok(())
}

pub async fn reset_dir(dir: impl AsRef<Path>) -> Result<()> {
    let dir = dir.as_ref();

    if exists(dir).await? {
        fs::remove_dir_all(dir).await.with_context(|| {
            format!("unable to remove directory and contents: {}", dir.display())
        })?;
    }

    fs::create_dir_all(dir)
        .await
        .with_context(|| format!("unable to create directory: {}", dir.display()))?;

    Ok(())
}

pub struct OwnedDir {
    path: PathBuf,
}

impl OwnedDir {
    pub fn new(path: impl Into<PathBuf>) -> Self {
        let path = path.into();

        Self { path }
    }

    pub fn path(&self) -> &Path {
        &self.path
    }

    pub async fn reset(&self) -> Result<()> {
        reset_dir(self.path()).await
    }

    pub async fn create_if_missing(&self) -> Result<()> {
        fs::create_dir_all(self.path())
            .await
            .with_context(|| format!("unable to create directory: {}", self.path().display()))?;
        Ok(())
    }

    pub async fn exists(&self) -> Result<bool> {
        exists(self.path()).await
    }
}

#[cfg(test)]
mod tests {
    use tempfile::tempdir;
    use tokio::{fs, stream::StreamExt};

    use super::*;

    async fn dir_len(dir: &Path) -> usize {
        let mut len = 0;
        let mut entries = fs::read_dir(dir).await.unwrap();

        while entries.next().await.is_some() {
            len += 1;
        }

        len
    }

    async fn fixture_existing_dir_with_one_file(dir: &Path) -> PathBuf {
        // Our owned dir exists on disk.
        fs::create_dir(&dir).await.unwrap();

        // It contains a file.
        let file_to_be_deleted = dir.join("delete-me.txt");
        fs::write(&file_to_be_deleted, "should be deleted upon dir reset")
            .await
            .unwrap();

        // We've checked that this is true.
        assert!(fs::metadata(&file_to_be_deleted).await.is_ok());
        assert_eq!(1, dir_len(&dir).await);

        file_to_be_deleted
    }

    #[tokio::test]
    async fn test_reset_missing() {
        let parent = tempdir().unwrap();

        let dir_path = parent.path().join("my-owned-dir");
        let dir = OwnedDir::new(&dir_path);

        dir.reset().await.unwrap();

        assert!(fs::metadata(&dir_path).await.is_ok());
    }

    #[tokio::test]
    async fn test_reset_existing() {
        let parent = tempdir().unwrap();

        let dir_path = parent.path().join("my-owned-dir");

        // When our owned dir exists on disk and contains a file,
        let file_to_be_deleted = fixture_existing_dir_with_one_file(&dir_path).await;

        let dir = OwnedDir::new(&dir_path);

        // And we reset the owned dir,
        dir.reset().await.unwrap();

        // Then the owned dir exists,
        assert!(fs::metadata(&dir_path).await.is_ok());

        // But it is empty.
        assert!(fs::metadata(&file_to_be_deleted).await.is_err());
        assert_eq!(0, dir_len(&dir_path).await);
    }

    #[tokio::test]
    async fn test_create_if_missing_missing() {
        let parent = tempdir().unwrap();

        let dir_path = parent.path().join("my-owned-dir");
        let dir = OwnedDir::new(&dir_path);

        dir.create_if_missing().await.unwrap();

        assert!(fs::metadata(&dir_path).await.is_ok());
    }

    #[tokio::test]
    async fn test_create_if_missing_existing() {
        let parent = tempdir().unwrap();

        let dir_path = parent.path().join("my-owned-dir");

        // When our owned dir exists on disk and contains a file,
        let file_to_be_deleted = fixture_existing_dir_with_one_file(&dir_path).await;

        let dir = OwnedDir::new(&dir_path);

        // And we create the owned dir is missing,
        dir.create_if_missing().await.unwrap();

        // Then the owned dir exists,
        assert!(fs::metadata(&dir_path).await.is_ok());

        // And its contents were preserved.
        assert!(fs::metadata(&file_to_be_deleted).await.is_ok());
        assert_eq!(1, dir_len(&dir_path).await);
    }

    #[tokio::test]
    async fn test_exists_missing() {
        let parent = tempdir().unwrap();

        let dir_path = parent.path().join("my-owned-dir");
        let dir = OwnedDir::new(&dir_path);

        assert!(!dir.exists().await.unwrap());
    }

    #[tokio::test]
    async fn test_exists_existing() {
        let parent = tempdir().unwrap();

        let dir_path = parent.path().join("my-owned-dir");

        fixture_existing_dir_with_one_file(&dir_path).await;

        let dir = OwnedDir::new(&dir_path);

        assert!(dir.exists().await.unwrap());
    }
}
