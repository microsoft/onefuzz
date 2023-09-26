// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::{BTreeMap, BTreeSet};

use std::num::NonZeroU32;

use anyhow::{Context, Result};

use debuggable_module::block::{sweep_region, Block, Blocks};
use debuggable_module::load_module::LoadModule;
use debuggable_module::loader::Loader;
use debuggable_module::path::FilePath;
use debuggable_module::{Module, Offset};
use symbolic::symcache::transform::{SourceLocation, Transformer};

use crate::allowlist::AllowList;
use crate::binary::BinaryCoverage;

pub use crate::binary::Count;

#[derive(Clone, Debug, Default, Eq, PartialEq)]
pub struct SourceCoverage {
    pub files: BTreeMap<FilePath, FileCoverage>,
}

#[derive(Clone, Debug, Default, Eq, PartialEq)]
pub struct FileCoverage {
    pub lines: BTreeMap<Line, Count>,
}

// Must be nonzero.
#[derive(Clone, Copy, Debug, Eq, Ord, PartialEq, PartialOrd)]
pub struct Line(NonZeroU32);

impl Line {
    pub fn new(number: u32) -> Result<Self> {
        NonZeroU32::try_from(number)
            .map(Self)
            .context("line numbers must be nonzero")
    }

    pub const fn number(&self) -> u32 {
        self.0.get()
    }
}

impl From<Line> for u32 {
    fn from(line: Line) -> Self {
        line.number()
    }
}

pub fn binary_to_source_coverage(
    binary: &BinaryCoverage,
    source_allowlist: &AllowList,
) -> Result<SourceCoverage> {
    use std::collections::btree_map::Entry;

    use symbolic::debuginfo::Object;
    use symbolic::symcache::{SymCache, SymCacheConverter};

    let loader = Loader::new();

    let mut source = SourceCoverage::default();

    for (exe_path, coverage) in &binary.modules {
        let module: Box<dyn Module> = Box::load(&loader, exe_path.clone())?;
        let debuginfo = module.debuginfo()?;

        let mut symcache = vec![];
        let mut converter = SymCacheConverter::new();

        if cfg!(windows) {
            use symbolic::symcache::transform::Function;
            struct CaseInsensitive {}
            impl Transformer for CaseInsensitive {
                fn transform_function<'f>(&'f mut self, f: Function<'f>) -> Function<'f> {
                    f
                }

                fn transform_source_location<'f>(
                    &'f mut self,
                    mut sl: SourceLocation<'f>,
                ) -> SourceLocation<'f> {
                    sl.file.name = sl.file.name.to_ascii_lowercase().into();
                    sl.file.directory = sl.file.directory.map(|d| d.to_ascii_lowercase().into());
                    sl.file.comp_dir = sl.file.comp_dir.map(|d| d.to_ascii_lowercase().into());
                    sl
                }
            }

            let case_insensitive_transformer = CaseInsensitive {};

            converter.add_transformer(case_insensitive_transformer);
        }

        let exe = Object::parse(module.executable_data())?;
        converter.process_object(&exe)?;

        let di = Object::parse(module.debuginfo_data())?;
        converter.process_object(&di)?;

        converter.serialize(&mut std::io::Cursor::new(&mut symcache))?;
        let symcache = SymCache::parse(&symcache)?;

        let mut blocks = Blocks::new();

        for function in debuginfo.functions() {
            for offset in coverage.as_ref().keys() {
                // Recover function blocks if it contains any coverage offset.
                if function.contains(offset) {
                    let function_blocks =
                        sweep_region(&*module, &debuginfo, function.offset, function.size)?;
                    blocks.extend(&function_blocks);
                    break;
                }
            }
        }

        for (offset, count) in coverage.as_ref() {
            // Inflate blocks.
            if let Some(block) = blocks.find(offset) {
                let block_offsets = instruction_offsets(&*module, block)?;

                for offset in block_offsets {
                    for location in symcache.lookup(offset.0) {
                        let Ok(line_number) = location.line().try_into() else {
                            continue; // line number was 0
                        };

                        if let Some(file) = location.file() {
                            // Only include relevant inlinees.
                            if !source_allowlist.is_allowed(&file.full_path()) {
                                continue;
                            }

                            let file_path = FilePath::new(file.full_path())?;

                            // We have a hit.
                            let file_coverage = source.files.entry(file_path).or_default();
                            let line = Line(line_number);

                            match file_coverage.lines.entry(line) {
                                Entry::Occupied(occupied) => {
                                    let old = occupied.into_mut();

                                    // If we miss any part of a line, count it as missed.
                                    let new = u32::max(old.0, count.0);

                                    *old = Count(new);
                                }
                                Entry::Vacant(vacant) => {
                                    vacant.insert(*count);
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    Ok(source)
}

fn instruction_offsets(module: &dyn Module, block: &Block) -> Result<BTreeSet<Offset>> {
    use iced_x86::Decoder;
    let data = module.read(block.offset, block.size)?;

    let mut offsets: BTreeSet<Offset> = BTreeSet::default();

    let mut pc = block.offset.0;
    let mut decoder = Decoder::new(64, data, 0);
    decoder.set_ip(pc);

    while decoder.can_decode() {
        let inst = decoder.decode();

        if inst.is_invalid() {
            break;
        }

        offsets.insert(Offset(pc));
        pc = inst.ip();
    }

    Ok(offsets)
}
