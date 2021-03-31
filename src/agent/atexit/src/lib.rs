// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use lazy_static::lazy_static;
use log::warn;
use std::sync::{Arc, RwLock};

struct AtExit {
    functions: RwLock<Vec<Box<dyn FnMut() + Send + Sync>>>,
}

lazy_static! {
    static ref ATEXIT: Arc<AtExit> = AtExit::new();
}

/// Register a function to run at exit (when `invoke` is called).
pub fn register<F: FnMut() + 'static + Send + Sync>(function: F) {
    ATEXIT.register_function(function)
}

/// Runs the registered functions and terminates the process with the specified exit `code`.
///
/// This function is not called automatically (e.g. via `drop`).
pub fn exit_process(code: i32) -> ! {
    ATEXIT.exit_process(code)
}

/// Runs the registered functions but does *not* terminate the process
///
/// This function is not called automatically (e.g. via `drop`).
pub fn execute() {
    ATEXIT.execute()
}

impl AtExit {
    fn new() -> Arc<Self> {
        let result = Arc::new(AtExit {
            functions: RwLock::new(vec![]),
        });
        {
            // This should cover the normal cases of pressing Ctrl+c or Ctrl+Break, but
            // we might fail to invoke the cleanup functions (e.g. to disable appverifier)
            // if the process is exiting from a logoff, machine reboot, or console closing event.
            //
            // The problem is the handler that `ctrlc` registers is not this handler, but instead
            // a handler that signals another thread to call our handler and then returns to the OS.
            // The OS might terminate our application before our handler actually runs.
            //
            // This is not a problem for Ctrl+c though because the OS won't terminate the program
            // (which is why we must exit ourselves.)
            let result = result.clone();
            ctrlc::set_handler(move || {
                warn!("Ctrl+c pressed - some results may not be saved.");
                result.exit_process(1);
            })
            .expect("More than one ctrl+c handler is not allowed");
        }
        result
    }

    fn register_function<F: FnMut() + 'static + Send + Sync>(&self, function: F) {
        self.functions.write().unwrap().push(Box::new(function));
    }

    fn exit_process(&self, code: i32) -> ! {
        self.execute();
        std::process::exit(code);
    }

    fn execute(&self) {
        for function in self.functions.write().unwrap().iter_mut() {
            function();
        }
    }
}
