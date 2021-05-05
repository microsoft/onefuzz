// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::BTreeSet;
use std::convert::TryInto;

use anyhow::Result;
use iced_x86::{Decoder, DecoderOptions, Instruction, Mnemonic, OpKind};

#[derive(Default)]
pub struct SancovDelimiters {
    llvm_bools_start: Option<u32>,
    llvm_bools_stop: Option<u32>,
    llvm_counters_start: Option<u32>,
    llvm_counters_stop: Option<u32>,
    llvm_pcs_start: Option<u32>,
    llvm_pcs_stop: Option<u32>,

    msvc_bools_start: Option<u32>,
    msvc_bools_stop: Option<u32>,
    msvc_counters_start: Option<u32>,
    msvc_counters_stop: Option<u32>,
    msvc_pcs_start: Option<u32>,
    msvc_pcs_stop: Option<u32>,
    msvc_preview_counters_start: Option<u32>,
    msvc_preview_counters_stop: Option<u32>,
}

// Define a partial accessor method that returns the named Sancov table region when
//
// 1. Both the `$start` and `$stop` delimiter symbols are present
// 2. The delimited region is non-empty
//
// Sancov `$start` delimiters are usually declared as 8 byte values to ensure that they predictably
// anchor the delimited table during linking. If `$pad` is true, adjust for this so that the `start`
// offset in the returned `SancovTable` denotes the actual offset of the first table entry.
macro_rules! define_table_getter {
    (
        name = $name: ident,
        start = $start: ident,
        stop = $stop: ident,
        ty = $ty: expr,
        pad = $pad: expr
    ) => {
        pub fn $name(&self) -> Option<SancovTable> {
            let offset = if $pad {
                self.$start?.checked_add(DELIMITER_START_PADDING)?
            } else {
                self.$start?
            };

            let size = self.$stop?.checked_sub(offset)?.try_into().ok()?;

            // The delimiters may be present even when the table is unused. We can detect this case
            // by an empty delimited region.
            if size == 0 {
                return None;
            }

            let ty = $ty;
            Some(SancovTable { ty, offset, size })
        }
    };
    // Accept trailing comma.
    (
        name = $name: ident,
        start = $start: ident,
        stop = $stop: ident,
        ty = $ty: expr,
        pad = $pad: expr,
    ) => {
        define_table_getter!(
            name = $name,
            start = $start,
            stop = $stop,
            ty = $ty,
            pad = $pad
        );
    };
}

impl SancovDelimiters {
    /// Return the most compiler-specific Sancov inline counter or bool flag table, if any.
    pub fn inline_table(&self) -> Option<SancovTable> {
        // With MSVC, the LLVM delimiters are typically linked in alongside the
        // MSVC-specific symbols. Check for MSVC-delimited tables first, though
        // our validation of table size _should_ make this unnecessary.

        if let Some(table) = self.msvc_bools_table() {
            return Some(table);
        }

        if let Some(table) = self.msvc_counters_table() {
            return Some(table);
        }

        if let Some(table) = self.msvc_preview_counters_table() {
            return Some(table);
        }

        // No MSVC tables found. Check for LLVM-emitted tables.

        if let Some(table) = self.llvm_bools_table() {
            return Some(table);
        }

        if let Some(table) = self.llvm_counters_table() {
            return Some(table);
        }

        None
    }

    /// Return the most compiler-specific PC table, if any.
    pub fn pcs_table(&self) -> Option<SancovTable> {
        // Check for MSVC tables first.
        if let Some(table) = self.msvc_pcs_table() {
            return Some(table);
        }

        if let Some(table) = self.llvm_pcs_table() {
            return Some(table);
        }

        None
    }

    define_table_getter!(
        name = llvm_bools_table,
        start = llvm_bools_start,
        stop = llvm_bools_stop,
        ty = SancovTableTy::Bools,
        pad = true,
    );

    define_table_getter!(
        name = llvm_counters_table,
        start = llvm_counters_start,
        stop = llvm_counters_stop,
        ty = SancovTableTy::Counters,
        pad = true,
    );

    define_table_getter!(
        name = llvm_pcs_table,
        start = llvm_pcs_start,
        stop = llvm_pcs_stop,
        ty = SancovTableTy::Pcs,
        pad = true,
    );

    define_table_getter!(
        name = msvc_bools_table,
        start = msvc_bools_start,
        stop = msvc_bools_stop,
        ty = SancovTableTy::Bools,
        pad = true,
    );

    define_table_getter!(
        name = msvc_counters_table,
        start = msvc_counters_start,
        stop = msvc_counters_stop,
        ty = SancovTableTy::Counters,
        pad = true,
    );

    define_table_getter!(
        name = msvc_pcs_table,
        start = msvc_pcs_start,
        stop = msvc_pcs_stop,
        ty = SancovTableTy::Pcs,
        pad = true,
    );

    define_table_getter!(
        name = msvc_preview_counters_table,
        start = msvc_preview_counters_start,
        stop = msvc_preview_counters_stop,
        ty = SancovTableTy::Counters,
        pad = true,
    );

    pub fn insert(&mut self, delimiter: Delimiter, offset: u32) {
        use Delimiter::*;

        let offset = Some(offset);

        match delimiter {
            LlvmBoolsStart => {
                self.llvm_bools_start = offset;
            }
            LlvmBoolsStop => {
                self.llvm_bools_stop = offset;
            }
            LlvmCountersStart => {
                self.llvm_counters_start = offset;
            }
            LlvmCountersStop => {
                self.llvm_counters_stop = offset;
            }
            LlvmPcsStart => {
                self.llvm_pcs_start = offset;
            }
            LlvmPcsStop => {
                self.llvm_pcs_stop = offset;
            }
            MsvcBoolsStart => {
                self.msvc_bools_start = offset;
            }
            MsvcBoolsStop => {
                self.msvc_bools_stop = offset;
            }
            MsvcCountersStart => {
                self.msvc_counters_start = offset;
            }
            MsvcCountersStop => {
                self.msvc_counters_stop = offset;
            }
            MsvcPcsStart => {
                self.msvc_pcs_start = offset;
            }
            MsvcPcsStop => {
                self.msvc_pcs_stop = offset;
            }
            MsvcPreviewCountersStart => {
                self.msvc_preview_counters_start = offset;
            }
            MsvcPreviewCountersStop => {
                self.msvc_preview_counters_stop = offset;
            }
        }
    }
}

/// A table of Sancov instrumentation data.
///
/// It is an array of either bytes or (packed pairs of) pointer-sized integers.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct SancovTable {
    pub ty: SancovTableTy,

    /// Module-relative offset of the first array element.
    pub offset: u32,

    /// Size of the array region (in bytes).
    ///
    /// For `u8`-sized elements, this is also the length, but for PC tables,
    /// this will be the product of the length and entry count, where each
    /// entry is defined in LLVM as:
    ///
    /// ```c
    /// struct PCTableEntry {
    ///   uintptr_t PC, PCFlags;
    /// };
    /// ```
    pub size: usize,
}

impl SancovTable {
    pub fn range(&self) -> std::ops::Range<u32> {
        self.offset..(self.offset + (self.size as u32))
    }
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum SancovTableTy {
    Bools,
    Counters,
    Pcs,
}

/// Note: on Windows, the LLVM `__start_` delimiter symbols do not denote the
/// first entry of a Sancov table array, but an anchor offset that precedes it
/// by 8 bytes.
///
/// See:
/// - `compiler-rt/lib/sanitizer_common/sanitizer_coverage_win_sections.cpp`
/// - `ModuleSanitizerCoverage::CreateSecStartEnd()` in
///   `llvm/lib/Transforms/Instrumentation/SanitizerCoverage.cpp:350-351`
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Delimiter {
    LlvmBoolsStart,
    LlvmBoolsStop,
    LlvmCountersStart,
    LlvmCountersStop,
    LlvmPcsStart,
    LlvmPcsStop,
    MsvcBoolsStart,
    MsvcBoolsStop,
    MsvcCountersStart,
    MsvcCountersStop,
    MsvcPcsStart,
    MsvcPcsStop,
    MsvcPreviewCountersStart,
    MsvcPreviewCountersStop,
}

/// Size of padding inserted (on Window) between `__start_` delimiter symbols
/// and the first entry of the delimited table's array.
///
/// To find the true start offset of the table, add this to the symbol value.
const DELIMITER_START_PADDING: u32 = 8;

impl std::str::FromStr for Delimiter {
    type Err = anyhow::Error;

    fn from_str(s: &str) -> Result<Self> {
        let delimiter = match s {
            "__start___sancov_cntrs" => Self::LlvmBoolsStart,
            "__stop___sancov_cntrs" => Self::LlvmBoolsStop,
            "__start___sancov_bools" => Self::LlvmCountersStart,
            "__stop___sancov_bools" => Self::LlvmCountersStop,
            "__start___sancov_pcs" => Self::LlvmPcsStart,
            "__stop___sancov_pcs" => Self::LlvmPcsStop,
            "__sancov$BoolFlagStart" => Self::MsvcBoolsStart,
            "__sancov$BoolFlagEnd" => Self::MsvcBoolsStop,
            "__sancov$8bitCountersStart" => Self::MsvcCountersStart,
            "__sancov$8bitCountersEnd" => Self::MsvcCountersStop,
            "__sancov$PCTableStart" => Self::MsvcPcsStart,
            "__sancov$PCTableEnd" => Self::MsvcPcsStop,
            "SancovBitmapStart" => Self::MsvcPreviewCountersStart,
            "SancovBitmapEnd" => Self::MsvcPreviewCountersStop,
            _ => {
                anyhow::bail!("string does not match any Sancov delimiter symbol");
            }
        };

        Ok(delimiter)
    }
}

#[derive(Clone, Debug)]
pub struct SancovInlineAccessScanner {
    base: u64,
    pub(crate) offsets: BTreeSet<u32>,
    table: SancovTable,
}

impl SancovInlineAccessScanner {
    pub fn new(base: u64, table: SancovTable) -> Self {
        let offsets = BTreeSet::default();

        Self { base, offsets, table }
    }

    pub fn scan(&mut self, data: &[u8], va: u64) -> Result<()> {
        let mut decoder = Decoder::new(64, data, DecoderOptions::NONE);

        decoder.set_ip(va);

        let mut inst = Instruction::default();
        while decoder.can_decode() {
            decoder.decode_out(&mut inst);

            match inst.op_code().mnemonic() {
                Mnemonic::Add | Mnemonic::Inc => {
                    // These may be 8-bit counter updates, check further.
                }
                Mnemonic::Mov => {
                    // This may be a bool flag set or the start of an unoptimized
                    // 8-bit counter update sequence.
                    //
                    //     mov al, [rel <table>]
                    //
                    // or:
                    //
                    //     mov [rel <table>], 1
                    match (inst.op0_kind(), inst.op1_kind()) {
                        (OpKind::Register, OpKind::Memory) => {
                            // Possible start of an unoptimized 8-bit counter update sequence, like:
                            //
                            //     mov al, [rel <table>]
                            //     add al, 1
                            //     mov [rel <table>], al
                            //
                            // Check the operand sizes.

                            if inst.memory_size().size() != 1 {
                                // Load would span multiple table entries, skip.
                                continue;
                            }

                            if inst.op0_register().size() != 1 {
                                // Should be unreachable after a 1-byte load.
                                continue;
                            }
                        }
                        (OpKind::Memory, OpKind::Immediate8) => {
                            // Possible bool flag set, like:
                            //
                            //     mov [rel <table>], 1
                            //
                            // Check store size and immediate value.

                            if inst.memory_size().size() != 1 {
                                // Store would span multiple table entries, skip.
                                continue;
                            }

                            if inst.immediate8() != 1 {
                                // Not a bool flag set, skip.
                                continue;
                            }
                        }
                        _ => {
                            // Not a known update pattern, skip.
                            continue;
                        }
                    }
                }
                _ => {
                    // Does not correspond to any known counter update, so skip.
                    continue;
                }
            }

            if inst.is_ip_rel_memory_operand() {
                // When relative, `memory_displacement64()` returns a VA. The
                // decoder RIP is already set to be module image-relative, so
                // our "VA" is already an RVA.
                let accessed = inst.memory_displacement64() as u32;

                if self.table.range().contains(&accessed) {
                    self.offsets.insert(inst.ip() as u32);
                }
            }
        }

        Ok(())
    }
}