// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use regex::RegexSet;
use serde::{Deserialize, Serialize};

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "snake_case")]
pub enum Filter {
    Allow(Allow),
    Deny(Deny),
}

impl Filter {
    pub fn is_allowed(&self, name: impl AsRef<str>) -> bool {
        match self {
            Self::Allow(f) => f.is_allowed(name),
            Self::Deny(f) => f.is_allowed(name),
        }
    }
}

impl Default for Filter {
    fn default() -> Self {
        Self::Allow(Allow::all())
    }
}

/// Filter that allows only those names which match a specific pattern.
#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(transparent)]
pub struct Allow {
    #[serde(with = "self::regex_set")]
    regexes: RegexSet,
}

impl Allow {
    /// Build a filter that allows all names.
    pub fn all() -> Self {
        let regexes = RegexSet::new(&[".*"]).unwrap();
        Self { regexes }
    }

    /// Build a filter that allows only the given patterns.
    ///
    /// If `exprs` is empty, then no names will be allowed.
    pub fn new(exprs: &[impl AsRef<str>]) -> Result<Self> {
        let regexes = RegexSet::new(exprs)?;
        Ok(Self { regexes })
    }

    /// Returns `true` if `name` is allowed.
    pub fn is_allowed(&self, name: impl AsRef<str>) -> bool {
        self.regexes.is_match(name.as_ref())
    }
}

impl Default for Allow {
    fn default() -> Self {
        Self::all()
    }
}

/// Filter that denies names which match a specific pattern.
#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(transparent)]
pub struct Deny {
    #[serde(with = "self::regex_set")]
    regexes: RegexSet,
}

impl Deny {
    /// Build a filter that allows all names.
    pub fn none() -> Self {
        let empty: &[&str] = &[];
        let regexes = RegexSet::new(empty).unwrap();
        Self { regexes }
    }

    /// Build a filter that denies only the given patterns.
    ///
    /// If `exprs` is empty, then no names will be denied.
    pub fn new(exprs: &[impl AsRef<str>]) -> Result<Self> {
        let regexes = RegexSet::new(exprs)?;
        Ok(Self { regexes })
    }

    /// Returns `true` if `name` is allowed.
    pub fn is_allowed(&self, name: impl AsRef<str>) -> bool {
        !self.regexes.is_match(name.as_ref())
    }
}

impl Default for Deny {
    fn default() -> Self {
        Self::none()
    }
}

mod regex_set {
    use std::fmt;

    use regex::RegexSet;
    use serde::de::{Deserializer, SeqAccess, Visitor};
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

            let regexes = RegexSet::new(patterns).unwrap();

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
