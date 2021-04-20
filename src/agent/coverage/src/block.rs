// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#[cfg(target_os = "linux")]
pub mod linux;

#[cfg(target_os = "windows")]
pub mod windows;

use std::collections::{btree_map, BTreeMap};

use serde::{Deserialize, Serialize};

use crate::code::ModulePath;

/// Block coverage for a command invocation.
///
/// Organized by module.
#[derive(Clone, Debug, Default, Deserialize, PartialEq, Serialize)]
#[serde(transparent)]
pub struct CommandBlockCov {
    modules: BTreeMap<ModulePath, ModuleCov>,
}

impl CommandBlockCov {
    /// Returns `true` if the module was newly-inserted (which initializes its
    /// block coverage map). Otherwise, returns `false`, and no re-computation
    /// is performed.
    pub fn insert(&mut self, path: &ModulePath, offsets: impl IntoIterator<Item = u32>) -> bool {
        use std::collections::btree_map::Entry;

        match self.modules.entry(path.clone()) {
            Entry::Occupied(_entry) => false,
            Entry::Vacant(entry) => {
                entry.insert(ModuleCov::new(offsets));
                true
            }
        }
    }

    pub fn increment(&mut self, path: &ModulePath, offset: u32) {
        let entry = self.modules.entry(path.clone());

        if let btree_map::Entry::Vacant(_) = entry {
            log::debug!(
                "initializing missing module when incrementing coverage at {}+{:x}",
                path,
                offset
            );
        }

        let module = entry.or_default();
        module.increment(offset);
    }

    pub fn iter(&self) -> impl Iterator<Item = (&ModulePath, &ModuleCov)> {
        self.modules.iter()
    }

    /// Total count of covered blocks across all modules.
    pub fn covered_blocks(&self) -> u64 {
        self.modules.values().map(|m| m.covered_blocks()).sum()
    }

    /// Total count of known blocks across all modules.
    pub fn known_blocks(&self) -> u64 {
        self.modules.values().map(|m| m.known_blocks()).sum()
    }

    pub fn merge_max(&mut self, other: &Self) {
        for (module, cov) in other.iter() {
            let entry = self.modules.entry(module.clone()).or_default();
            entry.merge_max(cov);
        }
    }

    /// Total count of blocks covered by modules in `self` but not `other`.
    ///
    /// Counts modules absent in `self`.
    pub fn difference(&self, other: &Self) -> u64 {
        let mut total = 0;

        for (module, cov) in &self.modules {
            if let Some(other_cov) = other.modules.get(module) {
                total += cov.difference(other_cov);
            } else {
                total += cov.covered_blocks();
            }
        }

        total
    }
}

#[derive(Clone, Debug, Default, Deserialize, Eq, PartialEq, Serialize)]
#[serde(transparent)]
pub struct ModuleCov {
    #[serde(with = "array")]
    pub blocks: BTreeMap<u32, BlockCov>,
}

impl ModuleCov {
    pub fn new(offsets: impl IntoIterator<Item = u32>) -> Self {
        let blocks = offsets.into_iter().map(|o| (o, BlockCov::new(o))).collect();
        Self { blocks }
    }

    /// Total count of blocks that have been reached (have a positive count).
    pub fn covered_blocks(&self) -> u64 {
        self.blocks.values().filter(|b| b.count > 0).count() as u64
    }

    /// Total count of known blocks.
    pub fn known_blocks(&self) -> u64 {
        self.blocks.len() as u64
    }

    /// Total count of blocks covered by `self` but not `other`.
    ///
    /// A difference of 0 does not imply identical coverage, and a positive
    /// difference does not imply that `self` covers every block in `other`.
    pub fn difference(&self, other: &Self) -> u64 {
        let mut total = 0;

        for (offset, block) in &self.blocks {
            if let Some(other_block) = other.blocks.get(offset) {
                if other_block.count == 0 {
                    total += u64::min(1, block.count as u64);
                }
            } else {
                total += u64::min(1, block.count as u64);
            }
        }

        total
    }

    pub fn increment(&mut self, offset: u32) {
        let block = self
            .blocks
            .entry(offset)
            .or_insert_with(|| BlockCov::new(offset));
        block.count = block.count.saturating_add(1);
    }

    pub fn merge_max(&mut self, other: &Self) {
        for block in other.blocks.values() {
            let entry = self
                .blocks
                .entry(block.offset)
                .or_insert_with(|| BlockCov::new(block.offset));
            entry.count = u32::max(entry.count, block.count);
        }
    }
}

/// Coverage info for a specific block, identified by its offset.
#[derive(Clone, Copy, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct BlockCov {
    /// Offset of the block, relative to the module base load address.
    //
    // These offsets come from well-formed executable modules, so we assume they
    // can be represented as `u32` values and losslessly serialized to an `f64`.
    //
    // If we need to handle malformed binaries or arbitrary addresses, then this
    // will need revision.
    pub offset: u32,

    /// Number of times a block was seen to be executed, relative to some input
    /// or corpus.
    ///
    /// Right now, we only set one-shot breakpoints, so the max `count` for a
    /// single input is 1. In this usage, if we measure corpus block coverage
    /// with `sum()` as the aggregation function, then `count` / `corpus.len()`
    /// tells us the proportion of corpus inputs that cover a block.
    ///
    /// If we reset breakpoints and recorded multiple block hits per input, then
    /// the corpus semantics would depend on the aggregation function.
    pub count: u32,
}

impl BlockCov {
    pub fn new(offset: u32) -> Self {
        Self { offset, count: 0 }
    }
}

mod array {
    use std::collections::BTreeMap;
    use std::fmt;

    use serde::de::{self, Deserializer, Visitor};
    use serde::ser::{SerializeSeq, Serializer};

    use super::BlockCov;

    type BlockCovMap = BTreeMap<u32, BlockCov>;

    pub fn serialize<S>(data: &BlockCovMap, ser: S) -> Result<S::Ok, S::Error>
    where
        S: Serializer,
    {
        let mut seq = ser.serialize_seq(Some(data.len()))?;
        for v in data.values() {
            seq.serialize_element(v)?;
        }
        seq.end()
    }

    pub fn deserialize<'d, D>(de: D) -> Result<BlockCovMap, D::Error>
    where
        D: Deserializer<'d>,
    {
        de.deserialize_seq(FlattenVisitor)
    }

    struct FlattenVisitor;

    impl<'d> Visitor<'d> for FlattenVisitor {
        type Value = BlockCovMap;

        fn expecting(&self, f: &mut fmt::Formatter) -> fmt::Result {
            write!(f, "array of blocks")
        }

        fn visit_seq<A>(self, mut seq: A) -> Result<Self::Value, A::Error>
        where
            A: de::SeqAccess<'d>,
        {
            let mut map = Self::Value::default();

            while let Some(block) = seq.next_element::<BlockCov>()? {
                map.insert(block.offset, block);
            }

            Ok(map)
        }
    }
}

#[cfg(test)]
mod tests {
    use anyhow::Result;
    use serde_json::json;

    use super::*;

    // Builds a `ModuleCov` from a vec of `(offset, count)` tuples.
    fn from_vec(data: Vec<(u32, u32)>) -> ModuleCov {
        let offsets = data.iter().map(|(o, _)| *o);
        let mut cov = ModuleCov::new(offsets);
        for (offset, count) in data {
            for _ in 0..count {
                cov.increment(offset);
            }
        }
        cov
    }

    // Builds a vec of `(offset, count)` tuples from a `ModuleCov`.
    fn to_vec(cov: &ModuleCov) -> Vec<(u32, u32)> {
        cov.blocks.iter().map(|(o, b)| (*o, b.count)).collect()
    }

    #[test]
    fn test_module_merge_max() {
        let initial = vec![(2, 0), (3, 0), (5, 0), (8, 0)];

        // Start out with known offsets and no hits.
        let mut total = from_vec(initial.clone());
        assert_eq!(to_vec(&total), vec![(2, 0), (3, 0), (5, 0), (8, 0),]);

        // If we merge data that is missing offsets, nothing happens.
        let empty = from_vec(vec![]);
        total.merge_max(&empty);
        assert_eq!(to_vec(&total), vec![(2, 0), (3, 0), (5, 0), (8, 0),]);

        // Merging some known hits updates the total.
        let hit_3_8 = from_vec(vec![(2, 0), (3, 1), (5, 0), (8, 1)]);
        total.merge_max(&hit_3_8);
        assert_eq!(to_vec(&total), vec![(2, 0), (3, 1), (5, 0), (8, 1),]);

        // Merging the same known hits again is idempotent.
        total.merge_max(&hit_3_8);
        assert_eq!(to_vec(&total), vec![(2, 0), (3, 1), (5, 0), (8, 1),]);

        // Monotonic: merging missed known offsets doesn't lose existing.
        let empty = from_vec(initial);
        total.merge_max(&empty);
        assert_eq!(to_vec(&total), vec![(2, 0), (3, 1), (5, 0), (8, 1),]);

        // Monotonic: merging some known hit, some misses doesn't lose existing.
        let hit_3 = from_vec(vec![(2, 0), (3, 1), (5, 0), (8, 0)]);
        total.merge_max(&hit_3);
        assert_eq!(to_vec(&total), vec![(2, 0), (3, 1), (5, 0), (8, 1),]);

        // Newly-discovered offsets are merged.
        let extra = from_vec(vec![
            (1, 0), // New, not hit
            (2, 0),
            (3, 1),
            (5, 0),
            (8, 1),
            (13, 1), // New, was hit
        ]);
        total.merge_max(&extra);
        assert_eq!(
            to_vec(&total),
            vec![(1, 0), (2, 0), (3, 1), (5, 0), (8, 1), (13, 1),]
        );
    }

    // Given a POSIX-style path as a string, construct a valid absolute path for
    // the target OS and return it as a checked `ModulePath`.
    fn module_path(posix_path: &str) -> Result<ModulePath> {
        let mut p = std::path::PathBuf::default();

        // Ensure that the new path is absolute.
        if cfg!(target_os = "windows") {
            p.push("c:\\");
        } else {
            p.push("/");
        }

        // Remove any affixed POSIX path separators, then split on any internal
        // separators and add each component to our accumulator path in an
        // OS-specific way.
        for c in posix_path.trim_matches('/').split('/') {
            p.push(c);
        }

        ModulePath::new(p)
    }

    fn cmd_cov_from_vec(data: Vec<(&ModulePath, Vec<(u32, u32)>)>) -> CommandBlockCov {
        let mut cov = CommandBlockCov::default();

        for (path, module_data) in data {
            let module_cov = from_vec(module_data);
            cov.modules.insert(path.clone(), module_cov);
        }

        cov
    }

    #[test]
    fn test_cmd_cov_increment() -> Result<()> {
        let main_exe = module_path("/onefuzz/main.exe")?;
        let some_dll = module_path("/common/some.dll")?;

        let mut coverage = CommandBlockCov::default();

        // Normal initialization, assuming disassembly of module.
        coverage.insert(&main_exe, vec![1, 20, 300].into_iter());
        coverage.increment(&main_exe, 20);

        // On-demand module initialization, using only observed offsets.
        coverage.increment(&some_dll, 123);
        coverage.increment(&some_dll, 456);
        coverage.increment(&some_dll, 789);

        let expected = cmd_cov_from_vec(vec![
            (&main_exe, vec![(1, 0), (20, 1), (300, 0)]),
            (&some_dll, vec![(123, 1), (456, 1), (789, 1)]),
        ]);

        assert_eq!(coverage, expected);

        Ok(())
    }

    #[test]
    fn test_cmd_cov_merge_max() -> Result<()> {
        let main_exe = module_path("/onefuzz/main.exe")?;
        let known_dll = module_path("/common/known.dll")?;
        let unknown_dll = module_path("/other/unknown.dll")?;

        let mut total = cmd_cov_from_vec(vec![
            (&main_exe, vec![(2, 0), (40, 1), (600, 0), (8000, 1)]),
            (&known_dll, vec![(1, 1), (30, 1), (500, 0), (7000, 0)]),
        ]);

        let new = cmd_cov_from_vec(vec![
            (&main_exe, vec![(2, 1), (40, 0), (600, 0), (8000, 0)]),
            (&known_dll, vec![(1, 0), (30, 0), (500, 1), (7000, 1)]),
            (&unknown_dll, vec![(123, 0), (456, 1)]),
        ]);

        total.merge_max(&new);

        let expected = cmd_cov_from_vec(vec![
            (&main_exe, vec![(2, 1), (40, 1), (600, 0), (8000, 1)]),
            (&known_dll, vec![(1, 1), (30, 1), (500, 1), (7000, 1)]),
            (&unknown_dll, vec![(123, 0), (456, 1)]),
        ]);

        assert_eq!(total, expected);

        Ok(())
    }

    #[test]
    fn test_block_cov_serde() -> Result<()> {
        let block = BlockCov {
            offset: 123,
            count: 456,
        };

        let ser = serde_json::to_string(&block)?;

        let text = r#"{"offset":123,"count":456}"#;

        assert_eq!(ser, text);

        let de: BlockCov = serde_json::from_str(&ser)?;

        assert_eq!(de, block);

        Ok(())
    }

    #[test]
    fn test_cmd_cov_serde() -> Result<()> {
        let main_exe = module_path("/onefuzz/main.exe")?;
        let some_dll = module_path("/common/some.dll")?;

        let cov = {
            let mut cov = CommandBlockCov::default();
            cov.insert(&main_exe, vec![1, 20, 300].into_iter());
            cov.increment(&main_exe, 1);
            cov.increment(&main_exe, 300);
            cov.insert(&some_dll, vec![2, 30, 400].into_iter());
            cov.increment(&some_dll, 30);
            cov
        };

        let ser = serde_json::to_string(&cov)?;

        let text = serde_json::to_string(&json!({
            some_dll.to_string(): [
                {"offset":2,"count":0},
                {"offset":30,"count":1},
                {"offset":400,"count":0},
            ],
            main_exe.to_string(): [
                {"offset":1,"count":1},
                {"offset":20,"count":0},
                {"offset":300,"count":1},
            ],
        }))?;

        assert_eq!(ser, text);

        let de: CommandBlockCov = serde_json::from_str(&ser)?;
        assert_eq!(de, cov);

        Ok(())
    }

    #[test]
    fn test_cmd_cov_stats() -> Result<()> {
        let main_exe = module_path("/onefuzz/main.exe")?;
        let some_dll = module_path("/common/some.dll")?;
        let other_dll = module_path("/common/other.dll")?;

        let empty = CommandBlockCov::default();

        let mut total: CommandBlockCov = serde_json::from_value(json!({
            some_dll.to_string(): [
                { "offset": 2, "count": 0 },
                { "offset": 30, "count": 1 },
                { "offset": 400, "count": 0 },
            ],
            main_exe.to_string(): [
                { "offset": 1, "count": 2 },
                { "offset": 20, "count": 0 },
                { "offset": 300, "count": 3 },
            ],
        }))?;

        assert_eq!(total.known_blocks(), 6);
        assert_eq!(total.covered_blocks(), 3);
        assert_eq!(total.covered_blocks(), total.difference(&empty));
        assert_eq!(total.difference(&total), 0);

        let new: CommandBlockCov = serde_json::from_value(json!({
            some_dll.to_string(): [
                { "offset": 2, "count": 0 },
                { "offset": 22, "count": 4 },
                { "offset": 30, "count": 5 },
                { "offset": 400, "count": 6 },
            ],
            main_exe.to_string(): [
                { "offset": 1, "count": 0 },
                { "offset": 300, "count": 1 },
                { "offset": 5000, "count": 0 },
            ],
            other_dll.to_string(): [
                { "offset": 123, "count": 0 },
                { "offset": 456, "count": 10 },
            ],
        }))?;

        assert_eq!(new.known_blocks(), 9);
        assert_eq!(new.covered_blocks(), 5);
        assert_eq!(new.covered_blocks(), new.difference(&empty));
        assert_eq!(new.difference(&new), 0);

        assert_eq!(new.difference(&total), 3);
        assert_eq!(total.difference(&new), 1);

        total.merge_max(&new);

        assert_eq!(total.known_blocks(), 10);
        assert_eq!(total.covered_blocks(), 6);

        Ok(())
    }
}
