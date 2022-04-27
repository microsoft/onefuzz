// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::time::Duration;

use anyhow::Result;
use tempfile::tempdir;
use tokio::fs;

use crate::monitor::DirectoryMonitor;

const TEST_TIMEOUT: Duration = Duration::from_millis(200);

macro_rules! timed_test {
    ($test_name: ident, $future: expr) => {
        #[tokio::test]
        async fn $test_name() -> Result<()> {
            let result = tokio::time::timeout(TEST_TIMEOUT, $future).await;
            result.map_err(|_| anyhow::anyhow!("test timed out after {:?}", TEST_TIMEOUT))?
        }
    };
}

timed_test!(test_monitor_empty_path, async move {
    let mut monitor = DirectoryMonitor::new("")?;

    assert!(monitor.start().await.is_err());

    Ok(())
});

timed_test!(test_monitor_nonexistent_path, async move {
    let mut monitor = DirectoryMonitor::new("some-nonexistent-path")?;

    assert!(monitor.start().await.is_err());

    Ok(())
});

timed_test!(test_monitor_file, async move {
    let dir = tempdir()?;

    // Create a file to erroneously watch.
    let file_path = dir.path().join("some-file.txt");
    tokio::fs::write(&file_path, "aaaaaa").await?;

    let mut monitor = DirectoryMonitor::new(&file_path)?;

    assert!(monitor.start().await.is_err());

    Ok(())
});

timed_test!(test_monitor_dir, async move {
    let dir = tempdir()?;
    let mut monitor = DirectoryMonitor::new(dir.path())?;

    assert!(monitor.start().await.is_ok());

    let _ = monitor.stop();

    Ok(())
});

timed_test!(test_monitor_dir_symlink, async move {
    let parent = tempdir()?;

    let child = parent.path().join("child");
    fs::create_dir(&child).await?;

    let symlink = parent.path().join("link-to-child");

    #[cfg(target_family = "unix")]
    fs::symlink(&child, &symlink).await?;

    #[cfg(target_family = "windows")]
    fs::symlink_dir(&child, &symlink).await?;

    let mut monitor = DirectoryMonitor::new(&symlink)?;

    assert!(monitor.start().await.is_ok());

    let _ = monitor.stop();

    Ok(())
});

timed_test!(test_monitor_dir_create_files, async move {
    use std::fs::canonicalize;

    let dir = tempdir()?;
    let mut monitor = DirectoryMonitor::new(dir.path())?;

    assert!(monitor.start().await.is_ok());

    let file_a = dir.path().join("a.txt");
    let file_b = dir.path().join("b.txt");
    let file_c = dir.path().join("c.txt");

    fs::write(&file_a, "aaa").await?;
    fs::write(&file_b, "bbb").await?;
    fs::write(&file_c, "ccc").await?;

    assert_eq!(monitor.next_file().await?, Some(canonicalize(&file_a)?));
    assert_eq!(monitor.next_file().await?, Some(canonicalize(&file_b)?));
    assert_eq!(monitor.next_file().await?, Some(canonicalize(&file_c)?));

    // TODO: on Windows, `notify` doesn't provide an event for the removal of a
    // watched directory, so we can't proactively close our channel.
    #[cfg(not(target_os = "windows"))]
    {
        dir.close()?;
        assert_eq!(monitor.next_file().await?, None);
    }

    let _ = monitor.stop();

    Ok(())
});
