// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use regex::{Regex, RegexSet};
use std::path::Path;

#[derive(Clone, Debug)]
pub struct AllowList {
    allow: RegexSet,
    deny: RegexSet,
}

impl AllowList {
    pub fn new(allow: RegexSet, deny: RegexSet) -> Self {
        Self { allow, deny }
    }

    pub fn load(path: impl AsRef<Path>) -> Result<Self> {
        let path = path.as_ref();
        let text = std::fs::read_to_string(path)?;
        Self::parse(&text)
    }

    pub fn parse(text: &str) -> Result<Self> {
        use std::io::{BufRead, BufReader};

        let reader = BufReader::new(text.as_bytes());

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

        // Allowed if rule-allowed but not excluded by a negative (deny) rule.
        self.allow.is_match(path) && !self.deny.is_match(path)
    }

    /// Build a new `Allowlist` that adds the allow and deny rules of `other` to `self`.
    pub fn extend(&mut self, other: &Self) {
        let allow = add_regexsets(&self.allow, &other.allow);
        let deny = add_regexsets(&self.deny, &other.deny);

        self.allow = allow;
        self.deny = deny;
    }
}

fn add_regexsets(lhs: &RegexSet, rhs: &RegexSet) -> RegexSet {
    let mut patterns = lhs.patterns().to_vec();
    patterns.extend(rhs.patterns().iter().map(|s| s.to_owned()));

    // Can't panic: patterns were accepted by input `RegexSet` ctors.
    RegexSet::new(patterns).unwrap()
}

impl Default for AllowList {
    fn default() -> Self {
        // Unwrap-safe due to valid constant expr.
        let allow = RegexSet::new([".*"]).unwrap();
        let deny = RegexSet::empty();

        AllowList::new(allow, deny)
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
    let expr = regex::escape(expr);

    // Translate escaped glob wildcards into quantified regexes.
    let expr = expr.replace(r"\*", ".*");

    // Anchor to line start and end.
    // On Windows we should also ignore case.
    let expr = if cfg!(windows) {
        format!("(?i)^{expr}$")
    } else {
        format!("^{expr}$")
    };

    Ok(Regex::new(&expr)?)
}

#[cfg(test)]
mod tests;
