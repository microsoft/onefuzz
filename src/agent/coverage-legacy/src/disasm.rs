// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::BTreeSet;
use std::convert::TryInto;

use anyhow::{bail, format_err, Context, Result};
use iced_x86::{Decoder, DecoderOptions, Instruction};

use crate::code::{ModuleIndex, Symbol};

pub struct ModuleDisassembler<'a> {
    module: &'a ModuleIndex,
    data: &'a [u8],
}

impl<'a> ModuleDisassembler<'a> {
    pub fn new(module: &'a ModuleIndex, data: &'a [u8]) -> Result<Self> {
        Ok(Self { module, data })
    }

    /// Find block entry points for every symbol in the module.
    pub fn find_blocks(&self) -> BTreeSet<u32> {
        let mut blocks = BTreeSet::new();

        for symbol in self.module.symbols.iter() {
            if let Err(err) = self.insert_symbol_blocks(&mut blocks, symbol) {
                log::error!(
                    "error disassembling blocks for symbol, err = {}, symbol = {:x?}",
                    err,
                    symbol
                );
            }
        }

        blocks
    }

    /// Find all entry points for blocks contained within the region of `symbol`.
    fn insert_symbol_blocks(&self, blocks: &mut BTreeSet<u32>, symbol: &Symbol) -> Result<()> {
        // Slice the symbol's instruction data from the module file data.
        let data = if let Some(data) = self.data.get(symbol.file_range_usize()) {
            data
        } else {
            bail!("data cannot contain file region for symbol");
        };

        // Initialize a decoder for the current symbol.
        let mut decoder = Decoder::new(64, data, DecoderOptions::NONE);

        // Compute the VA of the symbol, assuming preferred module base VA.
        let va = self
            .module
            .base_va
            .checked_add(symbol.image_offset)
            .ok_or_else(|| format_err!("symbol image offset overflowed base VA"))?;
        decoder.set_ip(va);

        // Function entry is a leader.
        blocks.insert(symbol.image_offset.try_into()?);

        let mut inst = Instruction::default();
        while decoder.can_decode() {
            decoder.decode_out(&mut inst);

            if let Some((target_va, conditional)) = branch_target(&inst) {
                let offset = target_va - self.module.base_va;

                // The branch target is a leader, if it is intra-procedural.
                if symbol.contains_image_offset(offset) {
                    blocks.insert(offset.try_into().context("ELF offset overflowed `u32`")?);
                }

                // Only mark the fallthrough instruction as a leader if the branch is conditional.
                // This will give an invalid basic block decomposition if the leaders we emit are
                // used as delimiters. In particular, blocks that end with a `jmp` will be too
                // large, and have an unconditional branch mid-block.
                //
                // However, we only care about the leaders as block entry points, so we can set
                // software breakpoints. These maybe-unreachable leaders are a liability wrt
                // mutating the running process' code, so we discard them for now.
                if conditional {
                    // The next instruction is a leader, if it exists.
                    if decoder.can_decode() {
                        // We decoded the current instruction, so the decoder offset is
                        // set to the next instruction.
                        let next = decoder.ip() as u64;
                        let next_offset =
                            if let Some(offset) = next.checked_sub(self.module.base_va) {
                                offset.try_into().context("ELF offset overflowed `u32`")?
                            } else {
                                anyhow::bail!("underflow converting ELF VA to offset")
                            };

                        blocks.insert(next_offset);
                    }
                }
            }
        }

        Ok(())
    }
}

// Returns the virtual address of a branch target, if present, with a flag that
// is true when the branch is conditional.
fn branch_target(inst: &Instruction) -> Option<(u64, bool)> {
    use iced_x86::FlowControl;

    match inst.flow_control() {
        FlowControl::ConditionalBranch => Some((inst.near_branch_target(), true)),
        FlowControl::UnconditionalBranch => Some((inst.near_branch_target(), false)),
        FlowControl::Call
        | FlowControl::Exception
        | FlowControl::IndirectBranch
        | FlowControl::IndirectCall
        | FlowControl::Interrupt
        | FlowControl::Next
        | FlowControl::Return
        | FlowControl::XbeginXabortXend => None,
    }
}
