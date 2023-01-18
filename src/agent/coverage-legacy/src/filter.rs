// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use regex::RegexSet;
use serde::{Deserialize, Serialize};

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "snake_case")]
pub enum Filter {
    Include(Include),
    Exclude(Exclude),
}

impl Filter {
    pub fn includes(&self, name: impl AsRef<str>) -> bool {
        match self {
            Self::Include(f) => f.includes(name),
            Self::Exclude(f) => f.includes(name),
        }
    }
}

impl Default for Filter {
    fn default() -> Self {
        Self::Include(Include::all())
    }
}

/// Filter that includes only those names which match a specific pattern.
#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(transparent)]
pub struct Include {
    #[serde(with = "self::regex_set")]
    regexes: RegexSet,
}

impl Include {
    /// Build a filter that includes only the given patterns.
    ///
    /// If `exprs` is empty, then no names will be included.
    pub fn new(exprs: &[impl AsRef<str>]) -> Result<Self> {
        let regexes = RegexSet::new(exprs)?;
        Ok(Self { regexes })
    }

    /// Build a filter that includes all names.
    pub fn all() -> Self {
        Self::new(&[".*"]).expect("error constructing filter from static, valid regex")
    }

    /// Returns `true` if `name` is included.
    pub fn includes(&self, name: impl AsRef<str>) -> bool {
        self.regexes.is_match(name.as_ref())
    }
}

impl Default for Include {
    fn default() -> Self {
        Self::all()
    }
}

impl From<Include> for Filter {
    fn from(include: Include) -> Self {
        Self::Include(include)
    }
}

/// Filter that excludes only those names which match a specific pattern.
#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(transparent)]
pub struct Exclude {
    #[serde(with = "self::regex_set")]
    regexes: RegexSet,
}

impl Exclude {
    /// Build a filter that excludes only the given patterns.
    ///
    /// If `exprs` is empty, then no names will be denied.
    pub fn new(exprs: &[impl AsRef<str>]) -> Result<Self> {
        let regexes = RegexSet::new(exprs)?;
        Ok(Self { regexes })
    }

    /// Build a filter that includes all names.
    pub fn none() -> Self {
        let empty: &[&str] = &[];
        Self::new(empty).expect("error constructing filter from static, empty regex set")
    }

    /// Returns `true` if `name` is included.
    pub fn includes(&self, name: impl AsRef<str>) -> bool {
        !self.regexes.is_match(name.as_ref())
    }
}

impl Default for Exclude {
    fn default() -> Self {
        Self::none()
    }
}

impl From<Exclude> for Filter {
    fn from(exclude: Exclude) -> Self {
        Self::Exclude(exclude)
    }
}

mod regex_set {
    use std::fmt;

    use regex::RegexSet;
    use serde::de::{self, Deserializer, SeqAccess, Visitor};
    use serde::ser::{SerializeSeq, Serializer};

    pub fn serialize<S>(regexes: &RegexSet, ser: S) -> Result<S::Ok, S::Error>
    where
        S: Serializer,
    {
        let patterns = regexes.patterns();
        let mut seq = ser.serialize_seq(Some(patterns.len()))?;
        for p in patterns {
            seq.serialize_element(p)?;
        }
        seq.end()
    }

    struct RegexSetVisitor;

    impl<'d> Visitor<'d> for RegexSetVisitor {
        type Value = RegexSet;

        fn expecting(&self, f: &mut fmt::Formatter) -> fmt::Result {
            write!(f, "a vec of strings which compile as regexes")
        }

        fn visit_seq<A>(self, mut seq: A) -> Result<Self::Value, A::Error>
        where
            A: SeqAccess<'d>,
        {
            let mut patterns = Vec::<String>::new();

            while let Some(p) = seq.next_element()? {
                patterns.push(p);
            }

            let regexes = RegexSet::new(patterns).map_err(de::Error::custom)?;

            Ok(regexes)
        }
    }

    pub fn deserialize<'d, D>(de: D) -> Result<RegexSet, D::Error>
    where
        D: Deserializer<'d>,
    {
        de.deserialize_seq(RegexSetVisitor)
    }
}
