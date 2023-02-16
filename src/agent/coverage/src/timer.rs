// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::sync::mpsc;
use std::thread;
use std::time::Duration;

use anyhow::{bail, Result};

pub fn timed<F, T>(timeout: Duration, function: F) -> Result<T>
where
    T: Send + 'static,
    F: FnOnce() -> T + Send + 'static,
{
    let (worker_sender, receiver) = mpsc::channel();
    let timer_sender = worker_sender.clone();

    let _worker = thread::spawn(move || {
        let out = function();
        let _ = worker_sender.send(Timed::Done(out));
    });

    let _timer = thread::spawn(move || {
        thread::sleep(timeout);
        let _ = timer_sender.send(Timed::Timeout);
    });

    match receiver.recv()? {
        Timed::Done(out) => Ok(out),
        Timed::Timeout => bail!("function exceeded timeout of {:?}", timeout),
    }
}

enum Timed<T> {
    Done(T),
    Timeout,
}
