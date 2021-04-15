// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::{fmt, path::PathBuf};

use anyhow::Result;
use onefuzz::{blob::BlobUrl, jitter::delay_with_jitter, syncdir::SyncedDir};
use reqwest::Url;
use tempfile::{tempdir, TempDir};
use tokio::{fs, time::Duration};

mod callback;
pub use callback::*;

const POLL_INTERVAL: Duration = Duration::from_secs(10);

#[cfg(test)]
mod tests;

#[derive(Debug)]
pub enum State<M> {
    Ready,
    Polled(Option<M>),
    Parsed(M, Url),
    Downloaded(M, Url, PathBuf, TempDir),
    Processed(M),
}

impl<M: PartialEq> PartialEq for State<M> {
    fn eq(&self, other: &State<M>) -> bool {
        use State::*;

        match (self, other) {
            (Ready, Ready) => true,
            (Polled(l), Polled(r)) => l == r,
            (Parsed(l0, l1), Parsed(r0, r1)) => l0 == r0 && l1 == r1,
            (Downloaded(l0, l1, l2, l3), Downloaded(r0, r1, r2, r3)) => {
                l0 == r0 && l1 == r1 && l2 == r2 && l3.path() == r3.path()
            }
            (Processed(l), Processed(r)) => l == r,
            _ => false,
        }
    }
}

impl<M> fmt::Display for State<M> {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        match self {
            State::Ready => write!(f, "Ready")?,
            State::Polled(..) => write!(f, "Polled")?,
            State::Parsed(..) => write!(f, "Parsed")?,
            State::Downloaded(..) => write!(f, "Downloaded")?,
            State::Processed(..) => write!(f, "Processed")?,
        }

        Ok(())
    }
}

pub enum Event<'a, M> {
    Poll(&'a mut dyn Queue<M>),
    Parse(&'a mut dyn Parser<M>),
    Download(&'a mut dyn Downloader),
    Process(&'a mut dyn Processor),
    Finish(&'a mut dyn Queue<M>),
}

impl<'a, M> fmt::Display for Event<'a, M> {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        match self {
            Event::Poll(..) => write!(f, "Poll")?,
            Event::Parse(..) => write!(f, "Parse")?,
            Event::Download(..) => write!(f, "Download")?,
            Event::Process(..) => write!(f, "Process")?,
            Event::Finish(..) => write!(f, "Finish")?,
        }

        Ok(())
    }
}

impl<'a, M> fmt::Debug for Event<'a, M> {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        write!(f, "{}", self)
    }
}

/// State machine that tries to poll a queue for new messages, parse a test
/// input URL from each message, download the test input, then process it.
///
/// The implementation of the transition actions are provided by impls of
/// callback traits.
///
/// Generic in the type `M` of the queue message. We assume `M` carries both
/// application data (here, the input URL, in some encoding) and metadata for
/// operations like finalizing a dequeue with a pop receipt.
pub struct InputPoller<M> {
    /// Internal automaton state.
    ///
    /// This is only nullable so we can internally `take()` the current state
    /// when scrutinizing it in the `trigger()` method.
    state: Option<State<M>>,

    batch_dir: Option<SyncedDir>,

    name: String,
}

impl<M> InputPoller<M> {
    pub fn new(name: impl AsRef<str>) -> Self {
        let name = name.as_ref().to_owned();
        let state = Some(State::Ready);
        Self {
            state,
            batch_dir: None,
            name,
        }
    }

    /// Process a given SyncedDir in batch
    pub async fn batch_process(
        &mut self,
        processor: &mut dyn Processor,
        to_process: &SyncedDir,
    ) -> Result<()> {
        self.batch_dir = Some(to_process.clone());
        to_process.init_pull().await?;
        info!(
            "batch processing directory: {} - {}",
            self.name,
            to_process.local_path.display()
        );

        let mut read_dir = fs::read_dir(&to_process.local_path).await?;
        while let Some(file) = read_dir.next_entry().await? {
            let path = file.path();
            info!(
                "processing batch-downloaded input: {} - {}",
                self.name,
                path.display()
            );

            // Compute the file name relative to the synced directory, and thus the
            // container.
            let blob_name = {
                let dir_path = to_process.local_path.canonicalize()?;
                let input_path = path.canonicalize()?;
                let dir_relative = input_path.strip_prefix(&dir_path)?;
                dir_relative.display().to_string()
            };
            let url = to_process.try_url().map(|x| x.blob(blob_name).url());

            processor.process(url, &path).await?;
        }
        Ok(())
    }

    /// Check if an input was already processed via batch-processing its container.
    pub async fn seen_in_batch(&self, url: &Url) -> Result<bool> {
        let result = if let Some(batch_dir) = &self.batch_dir {
            if let Ok(blob) = BlobUrl::new(url.clone()) {
                batch_dir.try_url().and_then(|u| u.account()) == blob.account()
                    && batch_dir.try_url().and_then(|u| u.container()) == blob.container()
                    && batch_dir.local_path.join(blob.name()).exists()
            } else {
                false
            }
        } else {
            false
        };
        Ok(result)
    }

    /// Get the current automaton state, including the state data.
    pub fn state(&self) -> &State<M> {
        self.state.as_ref().unwrap_or_else(|| unreachable!())
    }

    fn set_state(&mut self, state: impl Into<Option<State<M>>>) {
        self.state = state.into();
    }

    pub async fn run(&mut self, mut cb: impl Callback<M>) -> Result<()> {
        info!("starting input queue polling: {}", self.name);
        loop {
            match self.state() {
                State::Polled(None) => {
                    debug!("Input queue empty, sleeping");
                    delay_with_jitter(POLL_INTERVAL).await;
                }
                State::Downloaded(_msg, _url, input, _tempdir) => {
                    // if we can't get the filename, just pass the whole thing to logging
                    let filename = input.file_name().unwrap_or_else(|| input.as_ref());
                    info!(
                        "processing {} input: {}",
                        self.name,
                        filename.to_string_lossy()
                    );
                }
                _ => {}
            }

            self.next(&mut cb).await?;
        }
    }

    /// Transition to the next state in the poll loop, using `cb` to implement
    /// the transition actions.
    pub async fn next(&mut self, cb: &mut impl Callback<M>) -> Result<()> {
        use Event::*;
        use State::*;

        match self.state() {
            Ready => self.trigger(Poll(cb.queue())).await?,
            Polled(..) => self.trigger(Parse(cb.parser())).await?,
            Parsed(..) => self.trigger(Download(cb.downloader())).await?,
            Downloaded(..) => self.trigger(Process(cb.processor())).await?,
            Processed(..) => self.trigger(Finish(cb.queue())).await?,
        }

        Ok(())
    }

    /// Trigger a state transition event, and execute the action for each valid
    /// transition.
    ///
    /// The `Event` itself contains any callback functions and data needed to
    /// concretely implement the transition action.
    pub async fn trigger(&mut self, event: Event<'_, M>) -> Result<()> {
        // Take ownership of the current state so we can move its data out
        // of the variant.
        //
        // Invariant: `self.state.is_some()` on function entry.
        //
        // This local now repesents the current state, and we must not call
        // any other method on `self` that assumes `self.state.is_some()`.
        let state = self.state.take().unwrap();

        let result = self.try_trigger(state, event).await;

        if result.is_err() {
            // We must maintain a valid state, and we can logically recover from
            // any failed action or invalid transition.
            self.state = Some(State::Ready);
        }

        // Check that we always have a defined internal state.
        assert!(self.state.is_some());

        result
    }

    async fn try_trigger(&mut self, state: State<M>, event: Event<'_, M>) -> Result<()> {
        use Event::*;
        use State::*;

        match (state, event) {
            (Ready, Poll(queue)) => {
                let msg = queue.pop().await?;

                self.set_state(Polled(msg));
            }
            (Polled(msg), Parse(parser)) => {
                if let Some(msg) = msg {
                    let url = parser.parse(&msg)?;
                    self.set_state(Parsed(msg, url));
                } else {
                    self.set_state(Ready);
                }
            }
            (Parsed(msg, url), Download(downloader)) => {
                let download_dir = tempdir()?;
                if self.seen_in_batch(&url).await? {
                    debug!("url was seen during batch processing: {:?}", url);
                    self.set_state(Processed(msg));
                } else {
                    let input = downloader
                        .download(url.clone(), download_dir.path())
                        .await?;

                    self.set_state(Downloaded(msg, url, input, download_dir));
                }
            }
            // NOTE: _download_dir is a TempDir, which the physical path gets
            // deleted automatically upon going out of scope.  Keep it in-scope until
            // here.
            (Downloaded(msg, url, input, _download_dir), Process(processor)) => {
                processor.process(Some(url), &input).await?;

                self.set_state(Processed(msg));
            }
            (Processed(msg), Finish(queue)) => {
                queue.delete(msg).await?;

                self.set_state(Ready);
            }
            // We could panic here, and treat this case as a logic error.
            // However, we want users of this struct to be able to override the
            // default transition, so let them recover if they misuse it.
            (state, event) => bail!(
                "Invalid transition, state = {state}, event = {event}",
                state = state,
                event = event,
            ),
        }

        Ok(())
    }
}
