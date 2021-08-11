// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::cmp::Ordering;
use std::fmt;
use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};

/// A path and a line number
#[derive(Clone, Debug, Eq, Hash, PartialEq, Serialize, Deserialize)]
pub struct SrcLine {
    pub path: PathBuf,
    pub line: usize,
}

impl fmt::Display for SrcLine {
    fn fmt(&self, fmt: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(fmt, "{}:{}", &self.path.display(), self.line)
    }
}

impl Ord for SrcLine {
    fn cmp(&self, other: &Self) -> Ordering {
        let path_cmp = self.path.cmp(&other.path);

        if path_cmp != Ordering::Equal {
            return path_cmp;
        }

        self.line.cmp(&other.line)
    }
}

impl PartialOrd for SrcLine {
    fn partial_cmp(&self, other: &Self) -> Option<Ordering> {
        Some(self.cmp(other))
    }
}

impl SrcLine {
    pub fn new<P: AsRef<Path>>(path: P, line: usize) -> Self {
        let path = path.as_ref().to_owned();
        Self { path, line }
    }
}
