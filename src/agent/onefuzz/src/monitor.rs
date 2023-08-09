// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::{Path, PathBuf};

use anyhow::{format_err, Result};
use notify::{
    event::{CreateKind, ModifyKind, RenameMode},
    Event, EventKind, Watcher,
};
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
        let mut watcher =
            notify::recommended_watcher(move |event_or_err: notify::Result<Event>| {
                // pre-filter the events here
                let result = match event_or_err {
                    Ok(ev) => match ev.kind {
                        // we are interested in:
                        // - create
                        // - remove
                        // - modify name
                        EventKind::Create(_)
                        | EventKind::Remove(_)
                        | EventKind::Modify(ModifyKind::Name(_)) => Some(Ok(ev)),
                        // we are not interested in:
                        // - access
                        // - modify something else (data, metadata)
                        // - any other events
                        EventKind::Access(_)
                        | EventKind::Modify(_)
                        | EventKind::Any
                        | EventKind::Other => None,
                    },
                    Err(err) => Some(Err(err)),
                };

                if let Some(to_send) = result {
                    // A send error only occurs when the channel is closed. No remedial
                    // action is needed (or possible), so ignore it.
                    let _ = sender.send(to_send);
                }
            })?;

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

            let mut paths = event.paths.into_iter();

            match event.kind {
                EventKind::Create(create_kind) => {
                    let path = paths
                        .next()
                        .ok_or_else(|| format_err!("missing path for file create event"))?;

                    match create_kind {
                        CreateKind::File => {
                            return Ok(Some(path));
                        }
                        CreateKind::Folder => {
                            if self.report_directories {
                                return Ok(Some(path));
                            }
                        }
                        CreateKind::Any | CreateKind::Other => {
                            // Short-circuit and report this path if we're reporting everything.
                            if self.report_directories {
                                return Ok(Some(path));
                            }

                            match fs::metadata(&path).await {
                                Ok(metadata) => {
                                    // We're only reporting files, so make sure this is a file first.
                                    if metadata.is_file() {
                                        return Ok(Some(path));
                                    }
                                }
                                Err(e) => {
                                    warn!("failed to get metadata for {}: {:?}", path.display(), e);
                                }
                            }
                        }
                    }
                }
                EventKind::Modify(ModifyKind::Name(rename_mode)) => {
                    match rename_mode {
                        RenameMode::To => {
                            let path = paths.next().ok_or_else(|| {
                                format_err!("missing 'to' path for file rename-to event")
                            })?;

                            return Ok(Some(path));
                        }
                        RenameMode::Both => {
                            let _from = paths.next().ok_or_else(|| {
                                format_err!("missing 'from' path for file rename event")
                            })?;

                            let to = paths.next().ok_or_else(|| {
                                format_err!("missing 'to' path for file rename event")
                            })?;

                            return Ok(Some(to));
                        }
                        RenameMode::From => {
                            // ignore rename-from
                        }
                        RenameMode::Any | RenameMode::Other => {
                            // something unusual, ignore
                            info!(
                                "unknown rename event: ignoring {:?} for path {:?}",
                                rename_mode,
                                paths.next()
                            );
                        }
                    }
                }
                EventKind::Remove(..) => {
                    let path = paths
                        .next()
                        .ok_or_else(|| format_err!("missing path for file remove event"))?;

                    if path == self.dir {
                        // The directory we were watching was removed; we're done.
                        let _ = self.stop();
                        return Ok(None);
                    } else {
                        // Some file _inside_ the watched directory was removed. Ignore.
                    }
                }
                EventKind::Access(_) | EventKind::Modify(_) | EventKind::Other | EventKind::Any => {
                    unreachable!() // these events have already been filtered out
                }
            }
        }
    }
}

#[cfg(test)]
mod tests;
