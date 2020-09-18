// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![allow(clippy::new_without_default)]

use std::collections::hash_map::HashMap;

use crate::crash_detector::DebuggerResult;

/// The test summary includes counts of results for all inputs tested.
#[derive(Clone)]
pub struct Summary {
    /// Count of inputs tested with no exceptions or timeouts.
    passes: u32,

    /// Handled exceptions is the count of first_chance_exceptions where we never see
    /// the second chance exception (of which there is typically 1 - a crash.)
    handled_exceptions: u32,

    /// First chance exceptions observed.
    first_chance_exceptions: u32,

    /// Second chance exceptions observed.
    crashes: u32,

    /// Timeouts observed.
    timeouts: u32,
}

impl Summary {
    pub fn new() -> Self {
        Summary {
            passes: 0,
            handled_exceptions: 0,
            first_chance_exceptions: 0,
            crashes: 0,
            timeouts: 0,
        }
    }

    #[must_use]
    pub fn difference(&self, other: &Self) -> Self {
        Summary {
            passes: self.passes() - other.passes(),
            handled_exceptions: self.handled_exceptions() - other.handled_exceptions(),
            first_chance_exceptions: self.first_chance_exceptions()
                - other.first_chance_exceptions(),
            crashes: self.crashes() - other.crashes(),
            timeouts: self.timeouts() - other.timeouts(),
        }
    }

    pub fn passes(&self) -> u32 {
        self.passes
    }

    pub fn handled_exceptions(&self) -> u32 {
        self.handled_exceptions
    }

    pub fn first_chance_exceptions(&self) -> u32 {
        self.first_chance_exceptions
    }

    pub fn crashes(&self) -> u32 {
        self.crashes
    }

    pub fn timeouts(&self) -> u32 {
        self.timeouts
    }

    pub fn update(&mut self, result: &DebuggerResult) {
        if !result.any_crashes_or_timed_out() {
            self.passes += 1;
            return;
        }

        // Track first and second chance exceptions by stack hash so we
        // can see how many were caught.
        // We can't assume we see a first chance exception for every second chance,
        // one example is __fastfail - it only raises a second chance exception.
        let mut bug_hashmap: HashMap<u64, (u32, u32)> = HashMap::new();
        for exception in &result.exceptions {
            let (first, second) = if exception.first_chance {
                self.first_chance_exceptions += 1;
                (1, 0)
            } else {
                self.crashes += 1;
                (0, 1)
            };

            match bug_hashmap.get_mut(&exception.stack_hash) {
                Some(v) => {
                    *v = (v.0 + first, v.1 + second);
                }
                _ => {
                    bug_hashmap.insert(exception.stack_hash, (first, second));
                }
            }
        }

        for exception_counts in bug_hashmap.values() {
            if exception_counts.0 > 0 {
                self.handled_exceptions += exception_counts.0 - exception_counts.1;
            }
        }

        if result.timed_out() {
            self.timeouts += 1;
        }
    }
}

impl From<&DebuggerResult> for Summary {
    fn from(result: &DebuggerResult) -> Self {
        let mut summary = Summary::new();
        summary.update(result);
        summary
    }
}
