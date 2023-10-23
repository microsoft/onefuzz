use bad64::disasm;
use bad64::Op::*;

use anyhow::Result;
use std::collections::{BTreeMap, BTreeSet};

use crate::block::Block;
use crate::debuginfo::DebugInfo;
use crate::{Module, Offset};

use super::Blocks;

enum FlowControlOpClassification {
    Next,
    UnconditionalBranch,
    ConditionalBranch,
    Return,
    Call,
    Interrupt,
    NotFlowControl,

    // TODO: Don't know what to do about these yet
    IndirectBranch,
    IndirectCall,
    XbeginXabortXend,
    Exception,
}

impl From<bad64::Op> for FlowControlOpClassification {
    fn from(val: bad64::Op) -> Self {
        match val {
            NOP => FlowControlOpClassification::Next,
            B | BR => FlowControlOpClassification::UnconditionalBranch,
            CBNZ | CBZ | B_AL | B_CC | B_CS | B_EQ | B_GE | B_GT | B_HI | B_LE | B_LS | B_LT
            | B_MI | B_NE | B_NV | B_PL | B_VC | B_VS => {
                FlowControlOpClassification::ConditionalBranch
            }
            // TODO: Figure out what to do about BRKA BRKAS BRKB BRKBS BRKN BRKNS BRKPA BRKPAS BRKPB BRKPBS
            // equivalent to int3 in x86
            BRK => FlowControlOpClassification::Interrupt,
            RET => FlowControlOpClassification::Return,
            BL | BLR => FlowControlOpClassification::Call,
            SVC | HVC | SMC => FlowControlOpClassification::Interrupt,
            _ => FlowControlOpClassification::NotFlowControl,
        }
    }
}

pub fn sweep_region(
    module: &dyn Module,
    debuginfo: &DebugInfo,
    offset: Offset,
    size: u64,
) -> Result<Blocks> {
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
                let op_group: FlowControlOpClassification = inst.op().into();
                match op_group {
                    FlowControlOpClassification::UnconditionalBranch => {
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
                    FlowControlOpClassification::ConditionalBranch => {
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
                    FlowControlOpClassification::Interrupt => {
                        break;
                    }
                    FlowControlOpClassification::Return => {
                        break;
                    }
                    FlowControlOpClassification::Call => {
                        todo!()
                    }
                    _ => {
                        // println!("You didn't handle instruction type: {:?}", inst)
                    }
                }
            }
        }
    }

    let mut blocks = Blocks::default();

    for &entry in &visited {
        // Reset decoder for `entry`.
        let position = entry - offset.0;
        // let mut decoder = disasm(data, position);
        let mut end = 0;

        let mut decoder = disasm(data, position).peekable();
        while let Some(Ok(inst)) = decoder.next() {
            end = inst.address();
            let op_group: FlowControlOpClassification = inst.op().into();
            match op_group {
                FlowControlOpClassification::NotFlowControl => {}
                FlowControlOpClassification::IndirectBranch => {
                    break;
                }
                FlowControlOpClassification::UnconditionalBranch => {
                    break;
                }
                FlowControlOpClassification::ConditionalBranch => {
                    break;
                }
                FlowControlOpClassification::Return => {
                    break;
                }
                FlowControlOpClassification::Call => {
                    todo!();
                    // let target = Offset(inst.near_branch_target());

                    // // If call site is `noreturn`, then next instruction is not reachable.
                    // let noreturn = debuginfo
                    //     .functions()
                    //     .find(|f| f.contains(&target))
                    //     .map(|f| f.noreturn)
                    //     .unwrap_or(false);

                    // if noreturn {
                    //     break;
                    // }
                }
                FlowControlOpClassification::Exception => {
                    todo!();
                    // Ensure that the decoder PC points to the first instruction outside
                    // of the block.
                    //
                    // By doing this, we always exclude UD instructions from blocks.
                    // decoder.set_ip(inst.ip());

                    // Invalid instruction or UD.
                    // break;
                }
                FlowControlOpClassification::Interrupt => break,
                FlowControlOpClassification::Next => {
                    // Fall through.
                }
                FlowControlOpClassification::IndirectCall => {
                    // We dont' know the callee and can't tell if it is `noreturn`, so fall through.
                }
                FlowControlOpClassification::XbeginXabortXend => {
                    // Not yet analyzed, so fall through.
                }
            }

            // Based only on instruction semantics, we'd continue. But if the
            // next offset is a known block entrypoint, we're at a terminator.
            if let Some(Ok(next_inst)) = decoder.peek() {
                if visited.contains(&next_inst.address()) {
                    break;
                }
            }
        }

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
