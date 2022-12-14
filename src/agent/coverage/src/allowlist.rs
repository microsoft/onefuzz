// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use regex::Regex;

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
    allow: Vec<Regex>,
    deny: Vec<Regex>,
}

impl AllowList {
    pub fn is_allowed(&self, path: impl AsRef<str>) -> bool {
        let path = path.as_ref();

        match (self.allow.is_empty(), self.deny.is_empty()) {
            (false, false) => {
                // Allow only if rule-allowed but not also rule-denied.
                self.has_allow_match(path) && !self.has_deny_match(path)
            }
            (false, true) => {
                // Deny unless rule-allowed.
                self.has_allow_match(path)
            }
            (true, false) => {
                // Allow unless rule-denied.
                !self.has_deny_match(path)
            }
            (true, true) => {
                // Allow all.
                true
            }
        }
    }

    fn has_allow_match(&self, path: &str) -> bool {
        for re in &self.allow {
            if re.is_match(path) {
                return true;
            }
        }

        false
    }

    fn has_deny_match(&self, path: &str) -> bool {
        for re in &self.deny {
            if re.is_match(path) {
                return true;
            }
        }

        false
    }

    pub fn load(path: &str) -> Result<Self> {
        use std::fs::File;
        use std::io::{BufRead, BufReader};

        let file = File::open(path)?;
        let reader = BufReader::new(file);

        let mut allowlist = AllowList::default();

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
                            allowlist.allow.push(re);
                        }
                        Deny(re) => {
                            allowlist.deny.push(re);
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

        Ok(allowlist)
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
