// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use fixedbitset::FixedBitSet;
use iced_x86::{Decoder, DecoderOptions, FlowControl, Instruction, OpKind};

use crate::pe::TryInsert;

fn process_near_branch(instruction: &Instruction, blocks: &mut FixedBitSet) -> Result<()> {
    match instruction.op0_kind() {
        OpKind::NearBranch16 => {}
        OpKind::NearBranch32 => {}
        OpKind::NearBranch64 => {
            // Note we do not check if the branch takes us to another function, e.g.
            // with a tail call.
            //
            blocks.try_insert(instruction.near_branch_target() as usize)?;
        }
        OpKind::FarBranch16 => {}
        OpKind::FarBranch32 => {}
        _ => {}
    }

    Ok(())
}

pub fn find_blocks(
    bitness: u32,
    bytes: &[u8],
    func_rva: u32,
    blocks: &mut FixedBitSet,
) -> Result<()> {
    // We *could* maybe pass `DecoderOptions::AMD_BRANCHES | DecoderOptions::JMPE` because
    // we only care about control flow here, but it's not clear we'll ever see those instructions
    // and we don't need precise coverage so it doesn't matter too much.
    let mut decoder = Decoder::new(bitness, bytes, DecoderOptions::NONE);
    decoder.set_ip(func_rva as u64);

    let mut instruction = Instruction::default();
    while decoder.can_decode() {
        decoder.decode_out(&mut instruction);

        match instruction.flow_control() {
            FlowControl::Next => {}
            FlowControl::ConditionalBranch => {
                process_near_branch(&instruction, blocks)?;
                blocks.try_insert(instruction.next_ip() as usize)?;
            }
            FlowControl::UnconditionalBranch => {
                process_near_branch(&instruction, blocks)?;
            }
            FlowControl::IndirectBranch => {}
            FlowControl::Return => {}
            FlowControl::Call => {}
            FlowControl::IndirectCall => {}
            FlowControl::Interrupt => {}
            FlowControl::XbeginXabortXend => {}
            FlowControl::Exception => {}
        }
    }

    Ok(())
}
