// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

//! # srcview
//!
//! srcview is a crate for converting module+offset (modoff) coverage traces to source and line
//! number such that they can be visualized in an editor web frontend. It's job is split into
//! two parts: first creating a `SrcView` from debug info, then turning that `SrcView` and coverage into
//! a `Report`.
//!
//! ## SrcView
//!
//! There are two fundamental datatypes the crate defines, `ModOff` and `SrcLine`. `ModOff` is a module
//! name + offset from it's base, and SrcLine is an absolute path and line number. From a high
//! level, srcview converts modoff to srcline using debug info (PDBs only presently), then collects
//! and formats that debug info into a report. A `SrcView` is a collection of debug info (PdbCache)
//! mappings, and each debug info exposes four mappings:
//!  - ModOff to SrcLine (1:1)
//!  - Path to all valid SrcLines (1:n)
//!  - Path to all Symbols in that file (1:n)
//!  - Symbol to all valid SrcLines (1:n)
//!
//! A SrcView is a queryable collection of debug information, it _does not_ contain any coverage
//! information directly. It is simply the data extracted from the relevant debug structures in a
//! conviently queryable form.
//!
//! ## SrcView Examples
//!
//! ```text
//!              module                    offset
//!
//! ┌─────────┐
//! │         │           ┌──────────────┐
//! │ SrcView │ ─────┬───►│PdbCache - foo│
//! │         │      │    └──────────────┘         ┌──────────────────────────┐
//! └─────────┘      │                         ┌──►│SrcLine - z:\src\fizz.c:41│
//!                  │    ┌──────────────┐     │   │SrcLine - z:\src\fizz.c:42│
//!                  ├───►│PdbCache - bar├─────┤   └──────────────────────────┘
//!                  │    └──────────────┘     │
//!                  │                         │   ┌──────────────────────────┐
//!                  │    ┌──────────────┐     └──►│SrcLine - z:\src\quux.c:65│
//!                  └───►│PdbCache - baz│         └──────────────────────────┘
//!                       └──────────────┘
//! ```
//!
//! For example, bar+1234 might map to z:\src\quux.c:65.
//!
//! Absolute path to all valid SrcLine or Symbols in that path:
//! ```text
//!                <all>                    path
//! ┌─────────┐
//! │         │           ┌──────────────┐
//! │ SrcView │ ─────┬───►│PdbCache - foo│
//! │         │      │    └──────────────┘         ┌──────────────────────────┐
//! └─────────┘      │                         ┌──►│SrcLine - z:\src\fizz.c:41│
//!                  │    ┌──────────────┐     │   │SrcLine - z:\src\fizz.c:42│
//!                  ├───►│PdbCache - bar├─────┤   └──────────────────────────┘
//!                  │    └──────────────┘     │
//!                  │                         │   ┌──────────────────────────┐
//!                  │    ┌──────────────┐     └──►│SrcLine - z:\src\quux.c:65│
//!                  └───►│PdbCache - baz├──┐      └──────────────────────────┘
//!                       └──────────────┘  │
//!                                         │      ┌──────────────────────────┐
//!                                         └─────►│SrcLine - z:\src\fizz.c:43│
//!                                                └──────────────────────────┘
//! ```
//!
//! For example if we were asking for SrcLines, z:\src\fizz.c would iterate over all
//! pdbcache's and return a list of z:\src\fizz.c:41, z:\src\fizz.c:42, z:\src.fizz.c:65.
//! If we were asking for a list of symbols for a path, we might get back
//! FunctionOne, FunctionTwo, SomeGlobal, etc.
//!
//! Finally, we can query for symbol (e.g. `foo!FunctionOne`) to `SrcLine`. This functions
//! exactly like the others and will return a collection of `SrcLine`.
//!
//! ## Report
//!
//! Once we have a `SrcView`, we can combine that with the coverage set (i.e. `&[ModOff]`) to create
//! a `Report`. A `Report` is fundamentally the same info as a SrcView, except keyed around file paths
//! and with some statistics computed from the coverage info.
//!
//! Additionally, `Report` handles some of the messier parts:
//!  - Using regex's to filter `SrcView` contents, this lets you exclude things like the CRT from your
//!    output reports.
//!  - Using regex's to fixup input paths. All paths from PDBs are absolute, but most coverage
//!    visualization wants them to match a directory path. For example, if you have a git repo in
//!    ADO that is named 'test', it might be built on a build machine under 'z:\src\test`. To get
//!    the paths to match, we need to filter off 'z:\src\test'.
//!  - The actually emission of the coverage report itself. Right now this is only in Cobertura, but
//!    its not hard to imagine other formats being added.
//!  - Compute the coverage statistics on directories
//!
//! `Report` is significantly messier than `SrcView` and as of writing this I expect there to still be bugs.
//!
mod modoff;
mod pdbcache;
mod report;
mod srcline;
mod srcview;

pub use self::srcview::SrcView;
pub use modoff::{ModOff, ModOffParseError};
pub use pdbcache::PdbCache;
pub use report::Report;
pub use srcline::SrcLine;
