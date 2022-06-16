use std::{sync::mpsc, thread, time::{Duration, Instant}};

const MAX_POLL_PERIOD: Duration = Duration::from_millis(500);

pub struct Timer {
    sender: mpsc::Sender<()>,
    _handle: thread::JoinHandle<()>,
}

impl Timer {
    pub fn new<F, T>(timeout: Duration, on_timeout: F) -> Self
    where
        F: FnOnce() -> T + Send + 'static,
    {
        let (sender, receiver) = std::sync::mpsc::channel();

        let _handle = thread::spawn(move || {
            let poll_period = Duration::min(timeout, MAX_POLL_PERIOD);
            let start = Instant::now();

            while start.elapsed() < timeout {
                thread::sleep(poll_period);

                // Check if the timer has been cancelled.
                if let Err(mpsc::TryRecvError::Empty) = receiver.try_recv() {
                    continue;
                } else {
                    // We were cancelled or dropped, so return early and don't call back.
                    return;
                }
            }

            // Timed out, so call back.
            on_timeout();
        });

        Self { sender, _handle }
    }

    pub fn cancel(self) {
        // Drop `self`.
    }
}

impl Drop for Timer {
    fn drop(&mut self) {
        // Ignore errors, because they just mean the receiver has been dropped.
        let _ = self.sender.send(());
    }
}
