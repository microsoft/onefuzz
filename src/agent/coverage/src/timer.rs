// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::sync::mpsc;
use std::thread;
use std::time::Duration;

use thiserror::Error;

#[allow(dead_code)]
pub fn timed<F, T>(timeout: Duration, function: F) -> Result<T, TimerError>
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

    match receiver.recv() {
        Ok(Timed::Done(out)) => Ok(out),
        Ok(Timed::Timeout) => Err(TimerError::Timeout(timeout)),
        Err(recv) => Err(TimerError::Recv(recv)),
    }
}

enum Timed<T> {
    Done(T),
    Timeout,
}

#[derive(Debug, Error)]
pub enum TimerError {
    #[error("timer threads exited without sending messages")]
    Recv(mpsc::RecvError),

    #[error("function exceeded timeout of {0:?}")]
    Timeout(Duration),
}
