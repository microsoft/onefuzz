// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::{BTreeMap, BTreeSet};
use std::ops::Range;

use crate::Offset;

pub struct DebugInfo {
    functions: BTreeMap<Offset, Function>,
    labels: BTreeSet<Offset>,
}

impl DebugInfo {
    pub fn new(functions: BTreeMap<Offset, Function>, labels: Option<BTreeSet<Offset>>) -> Self {
        let labels = labels.unwrap_or_default();

        Self { functions, labels }
    }

    pub fn functions(&self) -> impl Iterator<Item = &Function> {
        self.functions.values()
    }

    pub fn labels(&self) -> impl Iterator<Item = Offset> + '_ {
        self.labels.iter().copied()
    }

    #[allow(clippy::manual_find)]
    pub fn find_function(&self, offset: Offset) -> Option<&Function> {
        // Search backwards from first function whose entrypoint is less than or
        // equal to `offset`.
        for f in self.functions.range(..=offset).map(|(_, f)| f).rev() {
            if f.contains(&offset) {
                return Some(f);
            }
        }

        None
    }
}

#[derive(Clone, Debug)]
pub struct Function {
    pub name: String,
    pub offset: Offset,
    pub size: u64,
    pub noreturn: bool,
}

impl Function {
    pub fn contains(&self, offset: &Offset) -> bool {
        let range = self.offset.region(self.size);
        range.contains(&offset.0)
    }

    pub fn range(&self) -> Range<Offset> {
        let lo = self.offset;
        let hi = Offset(lo.0.saturating_add(self.size));
        lo..hi
    }
}
