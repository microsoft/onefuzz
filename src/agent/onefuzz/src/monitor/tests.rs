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
    let dir = tempdir()?;
    let mut monitor = DirectoryMonitor::new(dir.path())?;

    assert!(monitor.start().await.is_ok());

    fs::write(dir.path().join("a.txt"), "aaa").await?;
    fs::write(dir.path().join("b.txt"), "bbb").await?;
    fs::write(dir.path().join("c.txt"), "ccc").await?;

    assert_eq!(monitor.next_file().await?, Some(dir.path().join("a.txt")));
    assert_eq!(monitor.next_file().await?, Some(dir.path().join("b.txt")));
    assert_eq!(monitor.next_file().await?, Some(dir.path().join("c.txt")));

    dir.close()?;

    assert_eq!(monitor.next_file().await?, None);

    let _ = monitor.stop();

    Ok(())
});
