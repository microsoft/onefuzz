// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::{BTreeMap, BTreeSet};
use std::fmt;
use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};

use anyhow::{format_err, Context, Result};
use log::warn;
use regex::Regex;
use xml::writer::{EmitterConfig, XmlEvent};

use crate::{SrcLine, SrcView};

#[derive(Clone, Debug, Eq, Hash, PartialEq)]
struct FileCov {
    symbols: BTreeMap<String, BTreeSet<SrcLine>>,
    lines: Vec<usize>,
    hits: Vec<usize>,
}

#[derive(Clone, Copy, Debug, Eq, Hash, PartialEq)]
struct DirCov {
    hits: usize,
    lines: usize,
}

impl DirCov {
    fn new(hits: usize, lines: usize) -> Self {
        Self { hits, lines }
    }
}

/// A path keyed structure to store both coverage and source info such that we can easily emit a
/// serialized representation of the source and its coverage
pub struct Report {
    filecov: BTreeMap<PathBuf, FileCov>,
    dircov: BTreeMap<PathBuf, DirCov>,
    overall: DirCov,
}

impl Report {
    /// Create a new report from coverage, a `SrcView`, and an optional regex
    ///
    /// # Arguments
    ///
    /// * `coverage` - The hit set of SrcLines
    /// * `srcview` - The total set of SrcLines
    /// * `include_regex` - A regular expression that will be applied against the file
    ///                     paths from the srcview. Any files matching this regex will be
    ///                     included in the output report. This is to exclude dependencies
    ///                     (e.g. the CRT) and should frequently be your project root. E.g.
    ///                     if you have git repo 'Foo' that was built on z:\src\Foo, a
    ///                     reasonable regex might be r'z:\\src\\Foo' (note that is is a
    ///                     raw string and the backslashes are to escape backreferences in
    ///                     the regex). A of `None` matches all files and includes them.
    ///
    /// # Errors
    ///
    /// If the regex cannot be compiled
    ///
    /// # Example
    /// ```no_run
    /// use srcview::{ModOff, Report, SrcLine, SrcView};
    ///
    /// let modoff_data = std::fs::read_to_string("coverage.modoff.txt").unwrap();
    /// let modoffs = ModOff::parse(&modoff_data).unwrap();
    ///
    /// let mut srcview = SrcView::new();
    /// srcview.insert("example.exe", "example.pdb").unwrap();
    ///
    /// let coverage: Vec<SrcLine> = modoffs
    ///     .into_iter()
    ///     .filter_map(|m| srcview.modoff(&m))
    ///     .collect();
    ///
    /// let r = Report::new(&coverage, &srcview, Some(r"E:\\1f\\coverage\\example")).unwrap();
    /// ```
    pub fn new(
        coverage: &[SrcLine],
        srcview: &SrcView,
        include_regex: Option<&str>,
    ) -> Result<Self> {
        let include = include_regex.map(Regex::new).transpose()?;
        let filecov = Self::compute_filecov(coverage, srcview, &include)?;

        // should this function take &[ModOff] and perform the conversion itself?

        let mut r = Self {
            filecov,
            // these will be populated by compute_dircov
            dircov: BTreeMap::new(),
            overall: DirCov::new(0, 0),
        };

        r.compute_dircov();

        Ok(r)
    }

    // should only be called from new, function to initalize file coverage
    fn compute_filecov(
        coverage: &[SrcLine],
        srcview: &SrcView,
        include: &Option<Regex>,
    ) -> Result<BTreeMap<PathBuf, FileCov>> {
        let uniq_cov: BTreeSet<SrcLine> = coverage.iter().cloned().collect();

        let mut filecov = BTreeMap::new();

        for path in srcview.paths() {
            if !Self::relevant_path(path, include)? {
                continue;
            }

            let path_srclocs: Vec<SrcLine> = srcview
                .path_lines(path)
                .ok_or_else(|| {
                    format_err!("unable to find path lines in path: {}", path.display())
                })?
                .map(|line| SrcLine::new(path, line))
                .collect();

            let mut lines = vec![];
            let mut hits = vec![];
            let mut symbols = BTreeMap::new();

            for srcloc in &path_srclocs {
                lines.push(srcloc.line);

                if uniq_cov.contains(srcloc) {
                    hits.push(srcloc.line);
                }
            }

            lines.sort_unstable();
            hits.sort_unstable();

            if let Some(path_symbols) = srcview.path_symbols(path) {
                for symbol in path_symbols {
                    let symbol_srclocs: BTreeSet<SrcLine> = srcview
                        .symbol(&symbol)
                        .ok_or_else(|| format_err!("unable to resolve symbol: {}", symbol))?
                        .cloned()
                        .collect();

                    symbols.insert(symbol, symbol_srclocs);
                }
            }

            filecov.insert(
                path.clone(),
                FileCov {
                    lines,
                    hits,
                    symbols,
                },
            );
        }

        Ok(filecov)
    }

    // should only be called from `new`, function to initialize directory coverage and overall
    // coverage. File coverage must be already initialized at this point
    fn compute_dircov(&mut self) {
        // need to make a copy so we don't hold an immutable reference to self in the loop
        let paths: Vec<PathBuf> = self.paths().cloned().collect();

        // on windows we can have multiple roots, e.g. c:\ and z:\. Thus we need to track all
        // the roots we see and their coverage to total them later for overall project coverage
        let mut overall: BTreeMap<PathBuf, DirCov> = BTreeMap::new();

        for path in paths {
            let mut anc = path.ancestors();

            // the /foo/bar/baz.c - discard baz.c
            let _ = anc.next();

            // for the rest of the directories, lets compute them
            for dir in anc {
                // if we've already computed it, were good
                if self.dir(dir).is_some() {
                    continue;
                }

                let mut hits = 0;
                let mut lines = 0;

                // get every file that matches this directory and total it
                for file in self.filter_files(dir) {
                    if let Some(cov) = self.file(&file) {
                        hits += cov.hits.len();
                        lines += cov.lines.len();
                    }
                }

                self.dircov
                    .insert(dir.to_path_buf(), DirCov::new(hits, lines));
            }

            // now lets get the root so we can compute the overall stats
            let anc = path.ancestors();

            if let Some(root) = anc.last() {
                // at this point we know we've computed this
                match self.dircov.get(root) {
                    Some(dircov) => {
                        // we don't really care if we're overwriting it
                        overall.insert(root.to_path_buf(), *dircov);
                    }
                    None => {
                        warn!(
                            "unable to get root for path for directory stats.  root: {}",
                            root.display()
                        );
                    }
                }
            }
        }

        let mut total = DirCov::new(0, 0);

        for dircov in overall.values() {
            total.hits += dircov.hits;
            total.lines += dircov.lines;
        }

        self.overall = total;
    }

    fn file<P: AsRef<Path>>(&self, path: P) -> Option<&FileCov> {
        self.filecov.get(path.as_ref())
    }

    fn dir<P: AsRef<Path>>(&self, path: P) -> Option<&DirCov> {
        self.dircov.get(path.as_ref())
    }

    fn paths(&self) -> impl Iterator<Item = &PathBuf> {
        self.filecov.keys()
    }

    fn dirs(&self) -> impl Iterator<Item = &PathBuf> {
        self.dircov.keys()
    }

    fn filter_files<P: AsRef<Path>>(&self, path: P) -> impl Iterator<Item = &PathBuf> {
        self.paths().filter(move |p| p.starts_with(path.as_ref()))
    }

    fn dir_has_files<P: AsRef<Path>>(&self, path: P) -> bool {
        for file in self.filter_files(path.as_ref()) {
            let mut anc = file.ancestors();
            let _ = anc.next();
            if let Some(dir) = anc.next() {
                if dir == path.as_ref() {
                    return true;
                }
            }
        }

        false
    }

    // wrapper to allow ergonomic filtering with an option
    fn filter_path<P: AsRef<Path> + fmt::Debug>(
        path: P,
        filter: &Option<Regex>,
    ) -> Result<PathBuf> {
        match filter {
            Some(regex) => {
                // we need our path as a string to regex it
                let path_string = path.as_ref().to_str().ok_or_else(|| {
                    format_err!("could not utf8 decode path: {}", path.as_ref().display())
                })?;

                let filtered = regex.replace(path_string, "").into_owned();

                Ok(PathBuf::from(filtered))
            }
            None => Ok(path.as_ref().to_path_buf()),
        }
    }

    // wrapper to allow ergonomic testing of our include regex inside an option against a
    // path
    fn relevant_path<P: AsRef<Path> + fmt::Debug>(
        path: P,
        include: &Option<Regex>,
    ) -> Result<bool> {
        match include {
            Some(regex) => {
                // we need our path as a string to regex it
                let path_string = path.as_ref().to_str().ok_or_else(|| {
                    format_err!("could not utf8 decode path: {}", path.as_ref().display())
                })?;

                Ok(regex.is_match(path_string))
            }
            None => Ok(true),
        }
    }

    /// Generate a Cobertura report
    ///
    /// # Arguments
    ///
    /// * `filter_regex` - This a search and replace regex that is applied to all file
    ///                    paths that will appear in the output report. This is specifically
    ///                    useful as many coverage visualization tools will require paths to
    ///                    match, and by default debug paths include the build machine info.
    ///                    For example, if our repo is 'Foo' and has `test.c` in it, the
    ///                    debug path could be `z:\build\Foo\test.c`. In the generated report
    ///                    we would want to strip off the build info and the repo name, such
    ///                    that the path that remains is relative to the repo root. As a
    ///                    result we might pass r"z:\\build\Foo\\". When applied to our SrcView
    ///                    paths this will replace that regex with the empty string, leaving the
    ///                    path `test.c` which relative to our repo root is correct. A value of
    ///                    `None` will not filter any paths.
    ///
    /// # Errors
    ///
    /// * If the filter regex cannot be compiled
    /// * If there is an error writing the output xml
    ///
    /// # Example
    ///
    /// ```no_run
    /// use srcview::{ModOff, Report, SrcLine, SrcView};
    ///
    /// let modoff_data = std::fs::read_to_string("coverage.modoff.txt").unwrap();
    /// let modoffs = ModOff::parse(&modoff_data).unwrap();
    ///
    /// let mut srcview = SrcView::new();
    /// srcview.insert("example.exe", "example.pdb").unwrap();
    ///
    /// let coverage: Vec<SrcLine> = modoffs
    ///     .into_iter()
    ///     .filter_map(|m| srcview.modoff(&m))
    ///     .collect();
    ///
    /// // in this case our repo is `coverage`, and has an `example` directory containing
    /// // our code files. Anything that matches this path shoudl be included.
    /// let r = Report::new(&coverage, &srcview, Some(r"E:\\1f\\coverage\\example")).unwrap();

    /// // However when generating the report, we want to strip off only the repo name --
    /// // `example` is inside the repo so to make the paths line up we need to leave it.
    /// let xml = r.cobertura(Some(r"E:\\1f\coverage\\")).unwrap();
    ///
    /// println!("{}", xml);
    /// ```
    pub fn cobertura(&self, filter_regex: Option<&str>) -> Result<String> {
        let filter = filter_regex.map(Regex::new).transpose()?;

        let mut backing: Vec<u8> = Vec::new();
        let mut ew = EmitterConfig::new()
            .perform_indent(true)
            .create_writer(&mut backing);

        // xml-rs does not support DTD entries yet, but thankfully ADO's parser is loose

        let unixtime = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .context("system time before unix epoch")?
            .as_secs();

        ew.write(
            XmlEvent::start_element("coverage")
                .attr("lines-valid", &format!("{}", self.overall.lines))
                .attr("lines-covered", &format!("{}", self.overall.hits))
                .attr(
                    "line-rate",
                    &format!(
                        "{:.02}",
                        self.overall.hits as f32 / self.overall.lines as f32
                    ),
                )
                .attr("branches-valid", "0")
                .attr("branches-covered", "0")
                .attr("branch-rate", "0")
                .attr("timestamp", &format!("{}", unixtime))
                .attr("complexity", "0")
                .attr("version", "0.1"),
        )?;
        ew.write(XmlEvent::start_element("sources"))?;
        ew.write(XmlEvent::start_element("source"))?;
        ew.write(XmlEvent::characters(""))?;
        ew.write(XmlEvent::end_element())?; // source
        ew.write(XmlEvent::end_element())?; // sources
        ew.write(XmlEvent::start_element("packages"))?;

        for dir in self.dirs() {
            if !self.dir_has_files(dir) {
                continue;
            }

            let display_dir = Self::filter_path(dir, &filter)?.display().to_string();

            ew.write(XmlEvent::start_element("package").attr("name", &display_dir))?;
            ew.write(XmlEvent::start_element("classes"))?;

            //
            // PER-FILE
            //

            for path in self.filter_files(dir) {
                let display_path = Self::filter_path(path, &filter)?.display().to_string();

                let filecov = match self.file(path) {
                    Some(filecov) => filecov,
                    None => {
                        warn!("unable to find coverage for path: {}", path.display());
                        continue;
                    }
                };

                let file_srclocs: BTreeSet<SrcLine> = filecov
                    .lines
                    .iter()
                    .map(|line| SrcLine::new(path, *line))
                    .collect();
                let hit_srclocs: BTreeSet<SrcLine> = filecov
                    .hits
                    .iter()
                    .map(|line| SrcLine::new(path, *line))
                    .collect();

                ew.write(
                    XmlEvent::start_element("class")
                        .attr("name", &display_path)
                        .attr("filename", &display_path)
                        .attr(
                            "line-rate",
                            &format!(
                                "{:.02}",
                                filecov.hits.len() as f32 / filecov.lines.len() as f32
                            ),
                        )
                        .attr("branch-rate", "0"),
                )?;

                //
                // METHODS
                //

                ew.write(XmlEvent::start_element("methods"))?;

                for (symbol, symbol_srclocs) in filecov.symbols.iter() {
                    let mut symbol_hits = 0;
                    for hit in &hit_srclocs {
                        if symbol_srclocs.contains(hit) {
                            symbol_hits += 1;
                        }
                    }

                    ew.write(
                        XmlEvent::start_element("method")
                            .attr("name", symbol)
                            .attr("signature", "")
                            .attr(
                                "line-rate",
                                &format!(
                                    "{:.02}",
                                    symbol_hits as f32 / symbol_srclocs.len() as f32
                                ),
                            )
                            .attr("branch-rate", "0"),
                    )?;
                    ew.write(XmlEvent::start_element("lines"))?;

                    for srcloc in symbol_srclocs {
                        let hits = if hit_srclocs.contains(srcloc) {
                            "1"
                        } else {
                            "0"
                        };

                        ew.write(
                            XmlEvent::start_element("line")
                                .attr("number", &format!("{}", srcloc.line))
                                .attr("hits", hits)
                                .attr("branch", "false"),
                        )?;
                        ew.write(XmlEvent::end_element())?; // line
                    }

                    ew.write(XmlEvent::end_element())?; // lines
                    ew.write(XmlEvent::end_element())?; // method
                }
                ew.write(XmlEvent::end_element())?; // methods
                ew.write(XmlEvent::start_element("lines"))?;

                //
                // LINES
                //
                for srcloc in &file_srclocs {
                    let hits = if hit_srclocs.contains(srcloc) {
                        "1"
                    } else {
                        "0"
                    };

                    ew.write(
                        XmlEvent::start_element("line")
                            .attr("number", &format!("{}", srcloc.line))
                            .attr("hits", hits)
                            .attr("branch", "false"),
                    )?;
                    ew.write(XmlEvent::end_element())?; // line
                }
                ew.write(XmlEvent::end_element())?; // lines
                ew.write(XmlEvent::end_element())?; // class
            }
            ew.write(XmlEvent::end_element())?; // classes
            ew.write(XmlEvent::end_element())?; // package
        }
        ew.write(XmlEvent::end_element())?; // packages
        ew.write(XmlEvent::end_element())?; // coverage

        Ok(String::from_utf8(backing)?)
    }
}
