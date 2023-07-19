// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::{
    io::ErrorKind,
    path::{Path, PathBuf},
};

use anyhow::{format_err, Result};
use notify::{Event, EventKind, Watcher};
use tokio::{
    fs,
    sync::mpsc::{unbounded_channel, UnboundedReceiver},
};

const DEFAULT_REPORT_DIRECTORIES: bool = false;

/// Watches a directory, and on file creation, emits the path to the file.
pub struct DirectoryMonitor {
    dir: PathBuf,
    notify_events: UnboundedReceiver<notify::Result<Event>>,
    watcher: notify::RecommendedWatcher,
    report_directories: bool,
}

impl DirectoryMonitor {
    /// Create a new directory monitor.
    ///
    /// The path `dir` must name a directory, not a file.
    pub async fn new(dir: impl AsRef<Path>) -> Result<Self> {
        use notify::RecursiveMode;

        // Canonicalize so we can compare the watched dir to paths in the events.
        let dir = fs::canonicalize(dir).await?;

        // Make sure we are watching a directory.
        //
        // This check will pass for symlinks to directories.
        if !fs::metadata(&dir).await?.is_dir() {
            bail!("monitored path is not a directory: {}", dir.display());
        }

        let (sender, notify_events) = unbounded_channel();
        let event_handler = move |event_or_err| {
            // A send error only occurs when the channel is closed. No remedial
            // action is needed (or possible), so ignore it.
            let _ = sender.send(event_or_err);
        };
        let mut watcher = notify::recommended_watcher(event_handler)?;
        watcher.watch(&dir, RecursiveMode::NonRecursive)?;

        Ok(Self {
            dir,
            notify_events,
            watcher,
            report_directories: DEFAULT_REPORT_DIRECTORIES,
        })
    }

    pub fn set_report_directories(&mut self, report_directories: bool) {
        self.report_directories = report_directories;
    }

    pub fn stop(&mut self) -> Result<()> {
        self.watcher.unwatch(&self.dir)?;
        Ok(())
    }

    pub async fn next_file(&mut self) -> Result<Option<PathBuf>> {
        loop {
            let event = match self.notify_events.recv().await {
                Some(Ok(event)) => event,
                Some(Err(err)) => {
                    // A low-level watch error has occurred. Treat as fatal.
                    warn!(
                        "error watching for new files. path = {}, error = {}",
                        self.dir.display(),
                        err
                    );

                    // Make sure we try to stop our `Watcher` if we return early.
                    let _ = self.stop();
                    return Ok(None);
                }
                None => {
                    // Make sure we try to stop our `Watcher` if we return early.
                    let _ = self.stop();
                    return Ok(None);
                }
            };

            match event.kind {
                EventKind::Create(..) => {
                    let path = event
                        .paths
                        .get(0)
                        .ok_or_else(|| format_err!("missing path for file create event"))?
                        .clone();

                    if self.report_directories {
                        return Ok(Some(path));
                    }

                    match fs::metadata(&path).await {
                        Ok(metadata) if metadata.is_file() => {
                            return Ok(Some(path));
                        }
                        Ok(_) => {
                            // Ignore directories.
                            continue;
                        }
                        Err(err) if err.kind() == ErrorKind::NotFound => {
                            // Ignore if deleted.
                            continue;
                        }
                        Err(err) => {
                            warn!(
                                "error checking metadata for file. path = {}, error = {}",
                                path.display(),
                                err
                            );
                            continue;
                        }
                    }
                }
                EventKind::Remove(..) => {
                    let path = event
                        .paths
                        .get(0)
                        .ok_or_else(|| format_err!("missing path for file remove event"))?;

                    if path == &self.dir {
                        // The directory we were watching was removed; we're done.
                        let _ = self.stop();
                        return Ok(None);
                    } else {
                        // Some file _inside_ the watched directory was removed. Ignore.
                    }
                }
                _event_kind => {
                    // Other filesystem event. Ignore.
                }
            }
        }
    }
}

#[cfg(test)]
mod tests;
