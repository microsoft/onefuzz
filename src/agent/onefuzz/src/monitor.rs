// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::PathBuf;
use std::sync::{self, mpsc::Receiver as SyncReceiver};
use std::thread::{self, JoinHandle};
use std::time::Duration;

use anyhow::Result;
use notify::{DebouncedEvent, Watcher};
use tokio::sync::mpsc::{unbounded_channel, UnboundedReceiver};

pub struct DirectoryMonitor {
    dir: PathBuf,
    notify_events: UnboundedReceiver<DebouncedEvent>,
    watcher: notify::RecommendedWatcher,
    terminated: bool,
}

impl DirectoryMonitor {
    pub fn new(dir: impl Into<PathBuf>) -> Self {
        let dir = dir.into();
        let (notify_sender, notify_receiver) = sync::mpsc::channel();
        let delay = Duration::from_millis(100);
        let watcher = notify::watcher(notify_sender, delay).unwrap();

        // We can drop the thread handle, and it will continue to run until it
        // errors or we drop the async receiver.
        let (notify_events, _handle) = into_async(notify_receiver);

        Self {
            dir,
            notify_events,
            watcher,
            terminated: false,
        }
    }

    pub fn start(&mut self) -> Result<()> {
        use notify::RecursiveMode;

        // Canonicalize so we can compare the watched dir to paths in the events.
        self.dir = std::fs::canonicalize(&self.dir)?;
        self.watcher.watch(&self.dir, RecursiveMode::NonRecursive)?;

        Ok(())
    }

    pub fn stop(&mut self) -> Result<()> {
        self.terminated = true;
        self.watcher.unwatch(self.dir.clone())?;
        Ok(())
    }

    pub async fn next_file(&mut self) -> Option<PathBuf> {
        loop {
            let event = self.notify_events.recv().await;

            if event.is_none() {
                // Make sure we stop our `Watcher` if we return early.
                let _ = self.stop();
            }

            match event? {
                DebouncedEvent::Create(path) => {
                    return Some(path);
                }
                DebouncedEvent::Remove(path) => {
                    if path == self.dir {
                        // The directory we were watching was removed; we're done.
                        let _ = self.stop();
                        return None;
                    } else {
                        // Some file _inside_ the watched directory was removed. Ignore.
                    }
                }
                _event => {
                    // Other filesystem event. Ignore.
                }
            }
        }
    }
}

fn into_async<T: Send + 'static>(
    sync_receiver: SyncReceiver<T>,
) -> (UnboundedReceiver<T>, JoinHandle<()>) {
    let (sender, receiver) = unbounded_channel();

    let handle = thread::spawn(move || {
        while let Ok(msg) = sync_receiver.recv() {
            if sender.send(msg).is_err() {
                // The async receiver is closed. We can't do anything else, so
                // drop this message and the sync receiver.
                break;
            }
        }
    });

    (receiver, handle)
}
