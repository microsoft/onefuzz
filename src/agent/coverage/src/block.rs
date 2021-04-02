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
    pub fn insert(&mut self, path: &ModulePath, offsets: impl Iterator<Item = u64>) -> bool {
        use std::collections::btree_map::Entry;

        match self.modules.entry(path.clone()) {
            Entry::Occupied(_entry) => false,
            Entry::Vacant(entry) => {
                entry.insert(ModuleCov::new(offsets));
                true
            }
        }
    }

    pub fn increment(&mut self, path: &ModulePath, offset: u64) {
        let entry = self.modules.entry(path.clone());

        if let btree_map::Entry::Vacant(_) = entry {
            log::warn!(
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

    pub fn merge_max(&mut self, other: &Self) {
        for (module, cov) in other.iter() {
            let entry = self.modules.entry(module.clone()).or_default();
            entry.merge_max(cov);
        }
    }
}

#[derive(Clone, Debug, Default, Deserialize, Eq, PartialEq, Serialize)]
#[serde(transparent)]
pub struct ModuleCov {
    #[serde(with = "array")]
    pub blocks: BTreeMap<u64, BlockCov>,
}

impl ModuleCov {
    pub fn new(offsets: impl Iterator<Item = u64>) -> Self {
        let blocks = offsets.map(|o| (o, BlockCov::new(o))).collect();
        Self { blocks }
    }

    pub fn increment(&mut self, offset: u64) {
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
    pub offset: u64,

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
    pub fn new(offset: u64) -> Self {
        Self { offset, count: 0 }
    }
}

mod array {
    use std::collections::BTreeMap;
    use std::fmt;

    use serde::de::{self, Deserializer, Visitor};
    use serde::ser::{SerializeSeq, Serializer};

    use super::BlockCov;

    type BlockCovMap = BTreeMap<u64, BlockCov>;

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
    use super::*;

    // Builds a `ModuleCov` from a vec of `(offset, counts)` tuples.
    fn from_vec(data: Vec<(u64, u32)>) -> ModuleCov {
        let offsets = data.iter().map(|(o, _)| *o);
        let mut cov = ModuleCov::new(offsets);
        for (offset, count) in data {
            for _ in 0..count {
                cov.increment(offset);
            }
        }
        cov
    }

    // Builds a vec of `(count, offset)` tuples from a `ModuleCov`.
    fn to_vec(cov: &ModuleCov) -> Vec<(u64, u32)> {
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

    fn module_path(path: &str) -> ModulePath {
        ModulePath::new(std::path::PathBuf::from(path)).unwrap()
    }

    fn cmd_cov_from_vec(data: Vec<(&'static str, Vec<(u64, u32)>)>) -> CommandBlockCov {
        let mut cov = CommandBlockCov::default();

        for (path, module_data) in data {
            let module_cov = from_vec(module_data);
            cov.modules.insert(module_path(path), module_cov);
        }

        cov
    }

    #[test]
    fn test_cmd_cov_increment() {
        #[cfg(target_os = "linux")]
        const MAIN_EXE: &str = "/main.exe";

        #[cfg(target_os = "linux")]
        const SOME_DLL: &str = "/lib/some.dll";

        #[cfg(target_os = "windows")]
        const MAIN_EXE: &str = r"c:\main.exe";

        #[cfg(target_os = "windows")]
        const SOME_DLL: &str = r"c:\lib\some.dll";

        let main_exe = module_path(MAIN_EXE);
        let some_dll = module_path(SOME_DLL);

        let mut coverage = CommandBlockCov::default();

        // Normal initialization, assuming disassembly of module.
        coverage.insert(&main_exe, vec![1, 20, 300].into_iter());
        coverage.increment(&main_exe, 20);

        // On-demand module initialization, using only observed offsets.
        coverage.increment(&some_dll, 123);
        coverage.increment(&some_dll, 456);
        coverage.increment(&some_dll, 789);

        let expected = cmd_cov_from_vec(vec![
            (MAIN_EXE, vec![(1, 0), (20, 1), (300, 0)]),
            (SOME_DLL, vec![(123, 1), (456, 1), (789, 1)]),
        ]);

        assert_eq!(coverage, expected);
    }

    #[test]
    fn test_cmd_cov_merge_max() {
        #[cfg(target_os = "linux")]
        const MAIN_EXE: &str = "/main.exe";

        #[cfg(target_os = "linux")]
        const KNOWN_DLL: &str = "/lib/known.dll";

        #[cfg(target_os = "linux")]
        const UNKNOWN_DLL: &str = "/usr/lib/unknown.dll";

        #[cfg(target_os = "windows")]
        const MAIN_EXE: &str = r"c:\main.exe";

        #[cfg(target_os = "windows")]
        const KNOWN_DLL: &str = r"c:\lib\known.dll";

        #[cfg(target_os = "windows")]
        const UNKNOWN_DLL: &str = r"c:\usr\lib\unknown.dll";

        let mut total = cmd_cov_from_vec(vec![
            (MAIN_EXE, vec![(2, 0), (40, 1), (600, 0), (8000, 1)]),
            (KNOWN_DLL, vec![(1, 1), (30, 1), (500, 0), (7000, 0)]),
        ]);

        let new = cmd_cov_from_vec(vec![
            (MAIN_EXE, vec![(2, 1), (40, 0), (600, 0), (8000, 0)]),
            (KNOWN_DLL, vec![(1, 0), (30, 0), (500, 1), (7000, 1)]),
            (UNKNOWN_DLL, vec![(123, 0), (456, 1)]),
        ]);

        total.merge_max(&new);

        let expected = cmd_cov_from_vec(vec![
            (MAIN_EXE, vec![(2, 1), (40, 1), (600, 0), (8000, 1)]),
            (KNOWN_DLL, vec![(1, 1), (30, 1), (500, 1), (7000, 1)]),
            (UNKNOWN_DLL, vec![(123, 0), (456, 1)]),
        ]);

        assert_eq!(total, expected);
    }

    #[test]
    fn test_block_cov_serde() {
        let block = BlockCov {
            offset: 123,
            count: 456,
        };

        let ser = serde_json::to_string(&block).unwrap();

        let text = r#"{"offset":123,"count":456}"#;
        assert_eq!(ser, text);

        let de: BlockCov = serde_json::from_str(&ser).unwrap();
        assert_eq!(de, block);
    }

    #[test]
    fn test_cmd_cov_serde() {
        #[cfg(target_os = "linux")]
        const MAIN_EXE: &str = "/main.exe";

        #[cfg(target_os = "linux")]
        const SOME_DLL: &str = "/lib/some.dll";

        #[cfg(target_os = "windows")]
        const MAIN_EXE: &str = r"c:\main.exe";

        #[cfg(target_os = "windows")]
        const SOME_DLL: &str = r"c:\lib\some.dll";

        let main_exe = module_path(MAIN_EXE);
        let some_dll = module_path(SOME_DLL);

        let cov = {
            let mut cov = CommandBlockCov::default();
            cov.insert(&main_exe, vec![1, 20, 300].into_iter());
            cov.increment(&main_exe, 1);
            cov.increment(&main_exe, 300);
            cov.insert(&some_dll, vec![2, 30, 400].into_iter());
            cov.increment(&some_dll, 30);
            cov
        };

        let ser = serde_json::to_string(&cov).unwrap();

        #[cfg(target_os = "linux")]
        let text = r#"{"/lib/some.dll":[{"offset":2,"count":0},{"offset":30,"count":1},{"offset":400,"count":0}],"/main.exe":[{"offset":1,"count":1},{"offset":20,"count":0},{"offset":300,"count":1}]}"#;

        #[cfg(target_os = "windows")]
        let text = r#"{"c:\\lib\\some.dll":[{"offset":2,"count":0},{"offset":30,"count":1},{"offset":400,"count":0}],"c:\\main.exe":[{"offset":1,"count":1},{"offset":20,"count":0},{"offset":300,"count":1}]}"#;

        assert_eq!(ser, text);

        let de: CommandBlockCov = serde_json::from_str(&ser).unwrap();
        assert_eq!(de, cov);
    }
}
