// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
#![allow(clippy::manual_flatten)]

use std::collections::{HashMap, HashSet};
use std::ffi::OsStr;
use std::io;
use std::path::Path;
use std::process::Command;

use lazy_static::lazy_static;
use regex::Regex;

const LD_LIBRARY_PATH: &str = "LD_LIBRARY_PATH";

pub fn find_missing(mut cmd: Command) -> Result<HashSet<MissingDynamicLibrary>, io::Error> {
    // Check for missing _linked_ dynamic libraries.
    //
    // We must do this first to avoid false positives or negatives when parsing `LD_DEBUG`
    // output. The debug output gets truncated when a linked shared library is not found,
    // since any in-progress searches are aborted.
    let library_path = explicit_library_path(&cmd);
    let linked = LinkedDynamicLibraries::search(cmd.get_program(), library_path)?;
    let missing_linked = linked.not_found();

    if !missing_linked.is_empty() {
        return Ok(missing_linked);
    }

    // Check for missing _loaded_ dynamic libraries.
    //
    // Invoke the command with `LD_DEBUG` set, and parse the debug output.
    cmd.env("LD_DEBUG", "libs");
    let output = cmd.output()?;
    let logs = LdDebugLogs::parse(&*output.stderr);

    Ok(logs.missing())
}

// Compute the `LD_LIBRARY_PATH` value that a `Command` sets, if any.
//
// If the command either inherits or unsets the variable, returns `None`.
fn explicit_library_path(cmd: &Command) -> Option<&OsStr> {
    let key_value = cmd
        .get_envs()
        .find(|(k, _)| *k == OsStr::new(LD_LIBRARY_PATH));

    // Inherits, return `None`.
    let key_value = key_value?;

    // Unsets, return `None`.
    let value = key_value.1?;

    Some(value)
}

#[derive(Clone, Debug, Eq, Hash, PartialEq)]
pub struct MissingDynamicLibrary {
    pub name: String,
}

/// Dynamic library searches, as extracted from the dynamic linker debug log output
/// obtained by setting `LD_DEBUG=libs`.
///
/// For more info about `LD_DEBUG`, see the docs for ld.so(8).
pub struct LdDebugLogs {
    pub searches: HashMap<LdDebugSearchQuery, LdDebugSearchResult>,
}

impl LdDebugLogs {
    /// Extract attempted library searches from the debug logs.
    ///
    /// A search query is detected on a thread if we find message like `find
    /// library=libmycode.so`.
    ///
    /// We mark a library as found if and only if we find a matching log message like
    /// `calling init: /path/to/libmycode.so`, on the same thread.
    ///
    /// This is only really useful for detecting `dlopen()` failures, when dynamic linking
    /// succeeds. If process startup fails due to a missing linked library dependency,
    /// then the dynamic linker's search will stop early, and debug logs will be logically
    /// truncated. When that happens, we can get both false negatives (we'll only see the
    /// _first_ missing library) and false positives (we won't see evidence of module
    /// initialization for libraries that _were_ found).
    pub fn parse<R: io::Read>(readable: R) -> Self {
        use std::io::prelude::*;

        let mut searches = HashMap::default();

        let reader = io::BufReader::new(readable);

        for line in reader.lines() {
            // If ok, line is valid UTF-8.
            if let Ok(line) = line {
                if let Some(query) = LdDebugSearchQuery::parse(&line) {
                    searches.insert(query, LdDebugSearchResult::NotFound);
                    continue;
                }

                if let Some(found) = FoundLibrary::parse(&line) {
                    let query = found.query();
                    let result = LdDebugSearchResult::Found(found);
                    searches.insert(query, result);
                    continue;
                }
            }
        }

        Self { searches }
    }

    pub fn missing(&self) -> HashSet<MissingDynamicLibrary> {
        let mut missing = HashSet::default();

        for (query, result) in &self.searches {
            if *result == LdDebugSearchResult::NotFound {
                let lib = MissingDynamicLibrary {
                    name: query.name.clone(),
                };
                missing.insert(lib);
            }
        }

        missing
    }
}

#[derive(Clone, Debug, Eq, Hash, PartialEq)]
pub struct LdDebugSearchQuery {
    /// PID of the thread where the search query occurred.
    pub pid: u32,

    /// Name of the shared library that was searched for.
    pub name: String,
}

impl LdDebugSearchQuery {
    pub fn parse(text: &str) -> Option<Self> {
        let captures = FIND_LIBRARY_RE.captures(text)?;

        let pid = captures.get(1)?.as_str().parse().ok()?;
        let name = captures.get(2)?.as_str().to_owned();

        Some(Self { pid, name })
    }
}

#[derive(Clone, Debug, Eq, PartialEq)]
pub enum LdDebugSearchResult {
    NotFound,
    Found(FoundLibrary),
}

#[derive(Clone, Debug, Eq, PartialEq)]
pub struct FoundLibrary {
    /// PID of the thread where the satisfied search query occurred.
    pub pid: u32,

    /// Absolute path of the found shared library.
    ///
    /// Guaranteed to have a file name (as a `Path`).
    pub path: String,
}

impl FoundLibrary {
    pub fn name(&self) -> String {
        // Can't panic due to check in `parse_found()`.
        let name = Path::new(&self.path).file_name().unwrap();

        // Can't panic because `self.path` is a `String`.
        let name = name.to_str().unwrap();

        name.to_owned()
    }

    pub fn query(&self) -> LdDebugSearchQuery {
        LdDebugSearchQuery {
            pid: self.pid,
            name: self.name(),
        }
    }
}

impl FoundLibrary {
    pub fn parse(text: &str) -> Option<Self> {
        let captures = INIT_LIBRARY_RE.captures(text)?;

        let pid = captures.get(1)?.as_str().parse().ok()?;
        let path = captures.get(2)?.as_str().to_owned();

        // Ensure `path` is a file path, and has a file name.
        Path::new(&path).file_name()?;

        Some(Self { pid, path })
    }
}

lazy_static! {
    // Captures thread PID, file name of requested library.
    static ref FIND_LIBRARY_RE: Regex =
        Regex::new(r"(\d+):\s+find library=(.+) \[\d+\]; searching").unwrap();

    // Captures thread PID, absolute path of found library.
    static ref INIT_LIBRARY_RE: Regex =
        Regex::new(r"(\d+):\s+calling init: (.+)").unwrap();

    // Captures shared library name, absolute path of found library.
    static ref LDD_FOUND: Regex =
        Regex::new(r"([^\s]+) => (.+) \(0x[0-9a-f]+\)").unwrap();

    // Captures shared library name.
    static ref LDD_NOT_FOUND: Regex =
        Regex::new(r"([^\s]+) => not found").unwrap();
}

struct LddFound {
    name: String,
    path: String,
}

impl LddFound {
    pub fn parse(text: &str) -> Option<Self> {
        let captures = LDD_FOUND.captures(text)?;

        let name = captures.get(1)?.as_str().to_owned();
        let path = captures.get(2)?.as_str().to_owned();

        Some(Self { name, path })
    }
}

struct LddNotFound {
    name: String,
}

impl LddNotFound {
    pub fn parse(text: &str) -> Option<Self> {
        let captures = LDD_NOT_FOUND.captures(text)?;

        let name = captures.get(1)?.as_str().to_owned();

        Some(Self { name })
    }
}

#[derive(Debug)]
pub struct LinkedDynamicLibraries {
    pub libraries: HashMap<String, Option<String>>,
}

impl LinkedDynamicLibraries {
    pub fn search(
        module: impl AsRef<OsStr>,
        library_path: Option<&OsStr>,
    ) -> Result<Self, io::Error> {
        let mut cmd = Command::new("ldd");
        cmd.arg(module);

        if let Some(library_path) = library_path {
            cmd.env(LD_LIBRARY_PATH, library_path);
        } else {
            cmd.env_remove(LD_LIBRARY_PATH);
        }

        let output = cmd.output()?;
        let linked = Self::parse(&*output.stdout);

        Ok(linked)
    }

    pub fn parse<R: io::Read>(readable: R) -> Self {
        use std::io::prelude::*;

        let mut libraries = HashMap::default();

        let reader = io::BufReader::new(readable);

        for line in reader.lines() {
            if let Ok(line) = line {
                if let Some(not_found) = LddNotFound::parse(&line) {
                    libraries.insert(not_found.name, None);
                }

                if let Some(found) = LddFound::parse(&line) {
                    libraries.insert(found.name, Some(found.path));
                }
            }
        }

        Self { libraries }
    }

    pub fn not_found(&self) -> HashSet<MissingDynamicLibrary> {
        let mut missing = HashSet::default();

        for linked in &self.libraries {
            if let (name, None) = linked {
                let name = name.clone();
                let lib = MissingDynamicLibrary { name };
                missing.insert(lib);
            }
        }

        missing
    }
}

#[cfg(test)]
mod tests;
