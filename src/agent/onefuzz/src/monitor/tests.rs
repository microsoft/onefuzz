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

macro_rules! expected_timeout_test {
    ($test_name: ident, $future: expr) => {
        #[tokio::test]
        async fn $test_name() -> Result<()> {
            let result = tokio::time::timeout(TEST_TIMEOUT, $future).await;

            result.expect_err("Expected test to time out");
            Ok(())
        }
    };
}

timed_test!(test_monitor_empty_path, async move {
    let monitor = DirectoryMonitor::new("").await;

    assert!(monitor.is_err());

    Ok(())
});

timed_test!(test_monitor_nonexistent_path, async move {
    let monitor = DirectoryMonitor::new("some-nonexistent-path").await;

    assert!(monitor.is_err());

    Ok(())
});

timed_test!(test_monitor_file, async move {
    let dir = tempdir()?;

    // Create a file to erroneously watch.
    let file_path = dir.path().join("some-file.txt");
    tokio::fs::write(&file_path, "aaaaaa").await?;

    let monitor = DirectoryMonitor::new(&file_path).await;

    // Ctor must fail.
    assert!(monitor.is_err());

    Ok(())
});

timed_test!(test_monitor_dir, async move {
    let dir = tempdir()?;

    // Ctor must succeed.
    let mut monitor = DirectoryMonitor::new(dir.path()).await?;

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

    // Ctor must succeed.
    let mut monitor = DirectoryMonitor::new(&symlink).await?;

    let _ = monitor.stop();

    Ok(())
});

timed_test!(test_monitor_dir_create_files, async move {
    use std::fs::canonicalize;

    let dir = tempdir()?;
    let mut monitor = DirectoryMonitor::new(dir.path()).await?;

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

expected_timeout_test!(test_monitor_default_ignores_dir, async move {
    let dir = tempdir().unwrap();
    let mut monitor = DirectoryMonitor::new(dir.path()).await?;

    let sub_dir = dir.path().join("test");
    dbg!(&sub_dir);
    fs::create_dir(&sub_dir).await?;

    monitor.next_file().await?;
    anyhow::Ok(())
});

timed_test!(test_monitor_set_report_directories, async move {
    use std::fs::canonicalize;

    let dir = tempdir().unwrap();
    let mut monitor = DirectoryMonitor::new(dir.path()).await?;
    monitor.set_report_directories(true);

    let sub_dir = dir.path().join("test");
    dbg!(&sub_dir);
    fs::create_dir(&sub_dir).await?;

    assert_eq!(monitor.next_file().await?, Some(canonicalize(&sub_dir)?));

    Ok(())
});
