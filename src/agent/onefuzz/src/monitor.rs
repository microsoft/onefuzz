// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::{path::PathBuf, pin::Pin, sync::mpsc, time::Duration};

use anyhow::Result;
use futures::{
    stream::{FusedStream, Stream},
    task::{self, Poll},
};
use notify::{DebouncedEvent, Watcher};
use std::sync::mpsc::TryRecvError;

pub struct DirectoryMonitor {
    dir: PathBuf,
    rx: mpsc::Receiver<DebouncedEvent>,
    watcher: notify::RecommendedWatcher,
    terminated: bool,
}

impl DirectoryMonitor {
    pub fn new(dir: impl Into<PathBuf>) -> Self {
        let dir = dir.into();
        let (tx, rx) = mpsc::channel();
        let delay = Duration::from_millis(100);
        let watcher = notify::watcher(tx, delay).unwrap();

        Self {
            dir,
            rx,
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

    pub fn poll_file(&mut self) -> Poll<Option<PathBuf>> {
        let poll = match self.rx.try_recv() {
            Ok(DebouncedEvent::Create(path)) => Poll::Ready(Some(path)),
            Ok(DebouncedEvent::Remove(path)) => {
                if path == self.dir {
                    // The directory we were watching was removed; we're done.
                    self.stop().ok();
                    Poll::Ready(None)
                } else {
                    // Some file _inside_ the watched directory was removed.
                    Poll::Pending
                }
            }
            Ok(_evt) => {
                // Filesystem event we can ignore.
                Poll::Pending
            }
            Err(TryRecvError::Empty) => {
                // Nothing to read, but sender still connected.
                Poll::Pending
            }
            Err(TryRecvError::Disconnected) => {
                // We'll never receive any more events; whatever happened, we're done.
                self.stop().ok();
                Poll::Ready(None)
            }
        };

        poll
    }
}

impl Stream for DirectoryMonitor {
    type Item = PathBuf;

    fn poll_next(mut self: Pin<&mut Self>, cx: &mut task::Context) -> Poll<Option<Self::Item>> {
        let poll = self.poll_file();
        cx.waker().wake_by_ref();
        poll
    }
}

impl FusedStream for DirectoryMonitor {
    fn is_terminated(&self) -> bool {
        self.terminated
    }
}
