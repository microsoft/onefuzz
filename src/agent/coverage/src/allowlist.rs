// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use regex::{Regex, RegexSet};
use std::path::Path;

#[derive(Clone, Debug, Default)]
pub struct TargetAllowList {
    pub functions: AllowList,
    pub modules: AllowList,
    pub source_files: AllowList,
}

impl TargetAllowList {
    pub fn new(modules: AllowList, source_files: AllowList) -> Self {
        // Allow all.
        let functions = AllowList::default();

        Self {
            functions,
            modules,
            source_files,
        }
    }
}

#[derive(Clone, Debug, Default)]
pub struct AllowList {
    allow: Option<RegexSet>,
    deny: Option<RegexSet>,
}

impl AllowList {
    pub fn new(allow: impl Into<Option<RegexSet>>, deny: impl Into<Option<RegexSet>>) -> Self {
        let allow = allow.into();
        let deny = deny.into();

        Self { allow, deny }
    }

    pub fn load(path: impl AsRef<Path>) -> Result<Self> {
        use std::fs::File;
        use std::io::{BufRead, BufReader};

        let path = path.as_ref();

        let file = File::open(path)?;
        let reader = BufReader::new(file);

        let mut allow = vec![];
        let mut deny = vec![];

        // We could just collect and pass to the `RegexSet` ctor.
        //
        // Instead, check each rule individually for diagnostic purposes.
        for (index, line) in reader.lines().enumerate() {
            let line = line?;

            match AllowListLine::parse(&line) {
                Ok(valid) => {
                    use AllowListLine::*;

                    match valid {
                        Blank | Comment => {
                            // Ignore.
                        }
                        Allow(re) => {
                            allow.push(re);
                        }
                        Deny(re) => {
                            deny.push(re);
                        }
                    }
                }
                Err(err) => {
                    // Ignore invalid lines, but warn.
                    let line_number = index + 1;
                    warn!("error at line {}: {}", line_number, err);
                }
            }
        }

        let allow = RegexSet::new(allow.iter().map(|re| re.as_str()))?;
        let deny = RegexSet::new(deny.iter().map(|re| re.as_str()))?;
        let allowlist = AllowList::new(allow, deny);

        Ok(allowlist)
    }

    pub fn is_allowed(&self, path: impl AsRef<str>) -> bool {
        let path = path.as_ref();

        match (&self.allow, &self.deny) {
            (Some(allow), Some(deny)) => allow.is_match(path) && !deny.is_match(path),
            (Some(allow), None) => {
                // Deny unless rule-allowed.
                allow.is_match(path)
            }
            (None, Some(deny)) => {
                // Allow unless rule-denied.
                !deny.is_match(path)
            }
            (None, None) => {
                // Allow all.
                true
            }
        }
    }
}

pub enum AllowListLine {
    Blank,
    Comment,
    Allow(Regex),
    Deny(Regex),
}

impl AllowListLine {
    pub fn parse(line: &str) -> Result<Self> {
        let line = line.trim();

        // Allow and ignore blank lines.
        if line.is_empty() {
            return Ok(Self::Blank);
        }

        // Support comments of the form `# <comment>`.
        if line.starts_with("# ") {
            return Ok(Self::Comment);
        }

        // Deny rules are of the form `! <rule>`.
        if let Some(expr) = line.strip_prefix("! ") {
            let re = glob_to_regex(expr)?;
            return Ok(Self::Deny(re));
        }

        // Try to interpret as allow rule.
        let re = glob_to_regex(line)?;
        Ok(Self::Allow(re))
    }
}

#[allow(clippy::single_char_pattern)]
fn glob_to_regex(expr: &str) -> Result<Regex> {
    // Don't make users escape Windows path separators.
    let expr = expr.replace(r"\", r"\\");

    // Translate glob wildcards into quantified regexes.
    let expr = expr.replace("*", ".*");

    // Anchor to line start.
    let expr = format!("^{expr}");

    Ok(Regex::new(&expr)?)
}
