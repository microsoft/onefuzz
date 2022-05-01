// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::PathBuf;

use anyhow::{format_err, Result};
use notify::{Event, EventKind, Watcher};
use tokio::{
    fs,
    sync::mpsc::{unbounded_channel, UnboundedReceiver},
};

pub struct DirectoryMonitor {
    dir: PathBuf,
    notify_events: UnboundedReceiver<notify::Result<Event>>,
    watcher: notify::RecommendedWatcher,
}

impl DirectoryMonitor {
    pub fn new(dir: impl Into<PathBuf>) -> Result<Self> {
        let dir = dir.into();
        let (sender, notify_events) = unbounded_channel();
        let event_handler = move |event_or_err| {
            // A send error only occurs when the channel is closed. No remedial
            // action is needed (or possible), so ignore it.
            let _ = sender.send(event_or_err);
        };
        let watcher = notify::recommended_watcher(event_handler)?;

        Ok(Self {
            dir,
            notify_events,
            watcher,
        })
    }

    pub async fn start(&mut self) -> Result<()> {
        use notify::RecursiveMode;

        // Canonicalize so we can compare the watched dir to paths in the events.
        self.dir = fs::canonicalize(&self.dir).await?;

        // Make sure we are watching a directory.
        //
        // This check will pass for symlinks to directories.
        if !fs::metadata(&self.dir).await?.is_dir() {
            bail!("monitored path is not a directory: {}", self.dir.display());
        }

        // Initialize the watcher.
        self.watcher.watch(&self.dir, RecursiveMode::NonRecursive)?;

        Ok(())
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

                    return Ok(Some(path));
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

#[cfg(not(target_os = "macos"))]
#[cfg(test)]
mod tests;
