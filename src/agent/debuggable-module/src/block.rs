// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use std::collections::{BTreeMap, BTreeSet};

use crate::debuginfo::DebugInfo;
use crate::{Module, Offset};

pub fn sweep_module(module: &dyn Module, debuginfo: &DebugInfo) -> Result<Blocks> {
    let mut blocks = Blocks::default();

    for function in debuginfo.functions() {
        let function_blocks = sweep_region(module, debuginfo, function.offset, function.size)?;
        blocks.map.extend(&function_blocks.map);
    }

    Ok(blocks)
}

pub fn sweep_region_arm(
    module: &dyn Module,
    debuginfo: &DebugInfo,
    offset: Offset,
    size: u64,
) -> Result<Blocks> {
    use bad64::disasm;
    use bad64::Op::*;
    let region = offset.region(size);
    let mut visited = BTreeSet::new();

    let data = module.read(offset, size)?;

    let mut pending = Vec::new();

    // Schedule the function entrypoint.
    pending.push(offset.0);

    // Schedule any extra jump labels in the target region.
    for label in debuginfo.labels() {
        // Don't duplicate function entrypoint.
        if label == offset {
            continue;
        }

        // Don't visit labels outside of the function region.
        if !region.contains(&label.0) {
            continue;
        }

        pending.push(label.0);

        while let Some(entry) = pending.pop() {
            if !region.contains(&entry) {
                continue;
            }

            if visited.contains(&entry) {
                continue;
            }

            visited.insert(entry);

            // Reset decoder for `entry`.
            let position = (entry - offset.0);
            let mut decoder = disasm(data, position);

            // Decode instructions (starting from `entry`) until we reach a block
            // terminator or run out of valid data.
            while let Some(Ok(inst)) = decoder.next() {
                match inst.op() {
                    // Unconditional branch
                    B | BR => {
                        // Pretty sure we only need the first operand?
                        let target = match inst.operands()[0] {
                            // Idk which one we need yet
                            e => {
                                // Using the operand, we should figure out where the branch is going to
                                println!("Got B | BR operand: {:?}", e);
                                7
                            }
                        };
                        pending.push(target);

                        // We can't fall through to the next instruction, so don't add it to
                        // the worklist.
                        break;
                    }
                    // Conditional branch
                    CBNZ | CBZ | B_AL | B_CC | B_CS | B_EQ | B_GE | B_GT | B_HI | B_LE | B_LS
                    | B_LT | B_MI | B_NE | B_NV | B_PL | B_VC | B_VS => {
                        // Pretty sure we only need the first operand?
                        let target = match inst.operands()[0] {
                            // Idk which one we need yet
                            e => {
                                // Using the operand, we should figure out where the branch is going to
                                println!("Got conditional branch operand: {:?}", e);
                                7
                            }
                        };
                        pending.push(target);

                        // We can fall through, so add to work list.
                        if let Some(Ok(next_inst)) = decoder.peekable().peek() {
                            pending.push(next_inst.address());
                        }

                        // Fall through not guaranteed, so this block is terminated.
                        break;
                    }
                    // TODO: Figure out what to do about BRKA BRKAS BRKB BRKBS BRKN BRKNS BRKPA BRKPAS BRKPB BRKPBS
                    // equivalent to int3 in x86
                    BRK => {
                        break;
                    }
                    // return
                    RET => {
                        break;
                    }
                    // call
                    BL | BLR => {} // exception
                    // interrupt
                    SVC | HVC | SMC => {}
                    _ => {
                        println!("You didn't handle instruction type: {:?}", inst)
                    }
                }
            }
        }
    }

    panic!()
}

pub fn sweep_region(
    module: &dyn Module,
    debuginfo: &DebugInfo,
    offset: Offset,
    size: u64,
) -> Result<Blocks> {
    use iced_x86::Code;
    use iced_x86::Decoder;
    use iced_x86::FlowControl::*;

    let region = offset.region(size);

    let data = module.read(offset, size)?;
    let mut decoder = Decoder::new(64, data, 0);

    let mut visited = BTreeSet::new();

    let mut pending = Vec::new();

    // Schedule the function entrypoint.
    pending.push(offset.0);

    // Schedule any extra jump labels in the target region.
    for label in debuginfo.labels() {
        // Don't duplicate function entrypoint.
        if label == offset {
            continue;
        }

        // Don't visit labels outside of the function region.
        if !region.contains(&label.0) {
            continue;
        }

        pending.push(label.0);
    }

    while let Some(entry) = pending.pop() {
        if !region.contains(&entry) {
            continue;
        }

        if visited.contains(&entry) {
            continue;
        }

        visited.insert(entry);

        // Reset decoder for `entry`.
        let position = (entry - offset.0) as usize;
        decoder.set_position(position)?;
        decoder.set_ip(entry);

        // Decode instructions (starting from `entry`) until we reach a block
        // terminator or run out of valid data.
        while decoder.can_decode() {
            let inst = decoder.decode();

            match inst.flow_control() {
                IndirectBranch => {
                    // Treat as an unconditional branch, discarding indirect target.
                    break;
                }
                UnconditionalBranch => {
                    // Target is an entrypoint.
                    let target = inst.ip_rel_memory_address();
                    pending.push(target);

                    // We can't fall through to the next instruction, so don't add it to
                    // the worklist.
                    break;
                }
                ConditionalBranch => {
                    // Target is an entrypoint.
                    let target = inst.ip_rel_memory_address();
                    pending.push(target);

                    // We can fall through, so add to work list.
                    pending.push(inst.next_ip());

                    // Fall through not guaranteed, so this block is terminated.
                    break;
                }
                Return => {
                    break;
                }
                Call => {
                    let target = Offset(inst.near_branch_target());

                    // If call site is `noreturn`, then next instruction is not reachable.
                    let noreturn = debuginfo
                        .functions()
                        .find(|f| f.contains(&target))
                        .map(|f| f.noreturn)
                        .unwrap_or(false);

                    if noreturn {
                        break;
                    }
                }
                Exception => {
                    // Invalid instruction or UD.
                    break;
                }
                Interrupt => {
                    if inst.code() == Code::Int3 {
                        // Treat as noreturn function call.
                        break;
                    }
                }
                Next => {
                    // Fall through.
                }
                IndirectCall => {
                    // We dont' know the callee and can't tell if it is `noreturn`, so fall through.
                }
                XbeginXabortXend => {
                    // Not yet analyzed, so fall through.
                }
            }
        }
    }

    let mut blocks = Blocks::default();

    for &entry in &visited {
        // Reset decoder for `entry`.
        let position = (entry - offset.0) as usize;
        decoder.set_position(position)?;
        decoder.set_ip(entry);

        while decoder.can_decode() {
            let inst = decoder.decode();

            if inst.is_invalid() {
                // Assume that the decoder PC is in an undefined state. Reset it so we can
                // just query the decoder to get the exclusive upper bound on loop exit.
                decoder.set_ip(inst.ip());
                break;
            }

            match inst.flow_control() {
                IndirectBranch => {
                    break;
                }
                UnconditionalBranch => {
                    break;
                }
                ConditionalBranch => {
                    break;
                }
                Return => {
                    break;
                }
                Call => {
                    let target = Offset(inst.near_branch_target());

                    // If call site is `noreturn`, then next instruction is not reachable.
                    let noreturn = debuginfo
                        .functions()
                        .find(|f| f.contains(&target))
                        .map(|f| f.noreturn)
                        .unwrap_or(false);

                    if noreturn {
                        break;
                    }
                }
                Exception => {
                    // Ensure that the decoder PC points to the first instruction outside
                    // of the block.
                    //
                    // By doing this, we always exclude UD instructions from blocks.
                    decoder.set_ip(inst.ip());

                    // Invalid instruction or UD.
                    break;
                }
                Interrupt => {
                    if inst.code() == Code::Int3 {
                        // Treat as noreturn function call.
                        break;
                    }
                }
                Next => {
                    // Fall through.
                }
                IndirectCall => {
                    // We dont' know the callee and can't tell if it is `noreturn`, so fall through.
                }
                XbeginXabortXend => {
                    // Not yet analyzed, so fall through.
                }
            }

            // Based only on instruction semantics, we'd continue. But if the
            // next offset is a known block entrypoint, we're at a terminator.
            if visited.contains(&inst.next_ip()) {
                break;
            }
        }

        let end = decoder.ip();
        let size = end.saturating_sub(entry);

        if size > 0 {
            let offset = Offset(entry);
            let block = Block::new(offset, size);
            blocks.map.insert(offset, block);
        } else {
            warn!("dropping empty block {:x}..{:x}", entry, end);
        }
    }

    Ok(blocks)
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct Block {
    pub offset: Offset,
    pub size: u64,
}

impl Block {
    pub fn new(offset: Offset, size: u64) -> Self {
        Self { offset, size }
    }

    pub fn contains(&self, offset: &Offset) -> bool {
        self.offset.region(self.size).contains(&offset.0)
    }
}

#[derive(Clone, Debug, Default)]
pub struct Blocks {
    pub map: BTreeMap<Offset, Block>,
}

impl Blocks {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn iter(&self) -> impl Iterator<Item = &Block> {
        self.map.values()
    }

    pub fn find(&self, offset: &Offset) -> Option<&Block> {
        self.map.values().find(|b| b.contains(offset))
    }

    pub fn extend<'b>(&mut self, blocks: impl IntoIterator<Item = &'b Block>) {
        for &b in blocks.into_iter() {
            self.map.insert(b.offset, b);
        }
    }
}

impl<'b> IntoIterator for &'b Blocks {
    type Item = &'b Block;
    type IntoIter = std::collections::btree_map::Values<'b, Offset, Block>;

    fn into_iter(self) -> Self::IntoIter {
        self.map.values()
    }
}
