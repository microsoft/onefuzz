// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::{
    fmt::{self, Display, Formatter},
    hash::{Hash, Hasher},
    path::Path,
};

use anyhow::Result;
use fnv::FnvHasher;
use log::trace;
use serde::{Serialize, Serializer};
use win_util::memory;
use winapi::{shared::minwindef::DWORD, um::winnt::HANDLE};

use crate::dbghelp::{self, DebugHelpGuard, ModuleInfo};

const UNKNOWN_MODULE: &str = "<UnknownModule>";

#[derive(Clone, Debug, Hash, PartialEq)]
pub enum DebugFunctionLocation {
    /// File/line if available
    ///
    /// Should be stable - ASLR and JIT should not change source position,
    /// but some precision is lost.
    ///
    /// We mitigate this loss of precision by collecting multiple samples
    /// for the same hash bucket.
    Line { file: String, line: u32 },

    /// Offset if line information not available.
    Offset { disp: u64 },
}

impl Display for DebugFunctionLocation {
    fn fmt(&self, formatter: &mut Formatter) -> fmt::Result {
        match self {
            DebugFunctionLocation::Line { file, line } => write!(formatter, "{}:{}", file, line)?,
            DebugFunctionLocation::Offset { disp } => write!(formatter, "0x{:x}", disp)?,
        };
        Ok(())
    }
}

#[derive(Clone, Debug, Hash, PartialEq)]
pub enum DebugStackFrame {
    Frame {
        function: String,
        location: DebugFunctionLocation,
    },
    CorruptFrame,
}

impl DebugStackFrame {
    pub fn new(function: String, location: DebugFunctionLocation) -> DebugStackFrame {
        DebugStackFrame::Frame { function, location }
    }

    pub fn corrupt_frame() -> DebugStackFrame {
        DebugStackFrame::CorruptFrame
    }

    pub fn is_corrupt_frame(&self) -> bool {
        match self {
            DebugStackFrame::Frame { .. } => false,
            DebugStackFrame::CorruptFrame => true,
        }
    }
}

impl Display for DebugStackFrame {
    fn fmt(&self, formatter: &mut Formatter) -> fmt::Result {
        match self {
            DebugStackFrame::Frame { function, location } => match location {
                DebugFunctionLocation::Line { file, line } => {
                    write!(formatter, "{} {}:{}", function, file, line)
                }
                DebugFunctionLocation::Offset { disp } => {
                    write!(formatter, "{}+0x{:x}", function, disp)
                }
            },
            DebugStackFrame::CorruptFrame => formatter.write_str("<corrupt frame(s)>"),
        }
    }
}

impl Serialize for DebugStackFrame {
    fn serialize<S>(&self, serializer: S) -> std::result::Result<S::Ok, S::Error>
    where
        S: Serializer,
    {
        serializer.serialize_str(&format!("{}", self))
    }
}

#[derive(Debug, PartialEq, Serialize)]
pub struct DebugStack {
    frames: Vec<DebugStackFrame>,
}

impl DebugStack {
    pub fn new(frames: Vec<DebugStackFrame>) -> DebugStack {
        DebugStack { frames }
    }

    pub fn frames(&self) -> &[DebugStackFrame] {
        &self.frames
    }

    pub fn stable_hash(&self) -> u64 {
        // Corrupted stacks and jit can result in stacks that vary from run to run, so we exclude
        // those frames and anything below them for a more stable hash.
        let first_unstable_frame = self.frames.iter().position(|f| match f {
            DebugStackFrame::Frame { function, .. } => function == UNKNOWN_MODULE,
            DebugStackFrame::CorruptFrame => true,
        });

        let count = if let Some(position) = first_unstable_frame {
            position.max(1)
        } else {
            self.frames.len()
        };

        let mut hasher = FnvHasher::default();
        self.frames[0..count].hash(&mut hasher);
        hasher.finish()
    }
}

impl Display for DebugStack {
    fn fmt(&self, formatter: &mut Formatter) -> fmt::Result {
        let mut first = true;
        for frame in self.frames() {
            if !first {
                writeln!(formatter)?;
            }
            first = false;
            write!(formatter, "{}", frame)?;
        }
        Ok(())
    }
}

fn get_function_location_in_module(
    dbghlp: &DebugHelpGuard,
    module_info: &ModuleInfo,
    process_handle: HANDLE,
    program_counter: u64,
    inline_context: DWORD,
) -> DebugStackFrame {
    if let Ok(sym_info) =
        dbghlp.sym_from_inline_context(process_handle, program_counter, inline_context)
    {
        let function = format!(
            "{}!{}",
            Path::new(module_info.name()).display(),
            sym_info.symbol()
        );

        let sym_line_info =
            dbghlp.sym_get_file_and_line(process_handle, program_counter, inline_context);

        let location = match sym_line_info {
            // Don't use file/line for these magic line numbers.
            Ok(ref sym_line_info) if !sym_line_info.is_fake_line_number() => {
                DebugFunctionLocation::Line {
                    file: sym_line_info.filename().to_string_lossy().into(),
                    line: sym_line_info.line_number(),
                }
            }

            _ => DebugFunctionLocation::Offset {
                disp: sym_info.displacement(),
            },
        };

        DebugStackFrame::new(function, location)
    } else {
        // No function - assume we have an exe with no pdb (so no exports). This should be
        // common, so we won't report an error. We do want a nice(ish) location though.
        let location = DebugFunctionLocation::Offset {
            disp: program_counter - module_info.base_address(),
        };
        DebugStackFrame::new(module_info.name().to_string_lossy().into(), location)
    }
}

fn get_frame_with_unknown_module(process_handle: HANDLE, program_counter: u64) -> DebugStackFrame {
    // We don't have any module information. If the memory is executable, we assume the
    // stack is still valid, perhaps we have jit code and we use the base of the allocation
    // to use for a synthetic RVA which is hopefully somewhat stable.
    //
    // Otherwise, assume the stack is corrupt.
    match memory::get_memory_info(process_handle, program_counter) {
        Ok(mi) => {
            if mi.is_executable() {
                let offset = program_counter
                    .checked_sub(mi.base_address())
                    .expect("logic error computing fake rva");

                let location = DebugFunctionLocation::Offset { disp: offset };
                DebugStackFrame::new(UNKNOWN_MODULE.into(), location)
            } else {
                DebugStackFrame::corrupt_frame()
            }
        }
        Err(e) => {
            // We expect corrupt stacks, so it's common to see failures with this api,
            // but do want a log we can turn on if needed.
            trace!("Error getting memory info: {}", e);
            DebugStackFrame::corrupt_frame()
        }
    }
}

pub fn get_stack(
    process_handle: HANDLE,
    thread_handle: HANDLE,
    resolve_symbols: bool,
) -> Result<DebugStack> {
    let dbghlp = dbghelp::lock()?;

    let mut stack = vec![];

    dbghlp.stackwalk_ex(process_handle, thread_handle, |frame_context, frame| {
        // The program counter is the return address, potentially outside of the function
        // performing the call. We subtract 1 to ensure the address is within the call.
        let program_counter = frame_context.program_counter().saturating_sub(1);

        let debug_stack_frame = if resolve_symbols {
            if let Ok(module_info) = dbghlp.sym_get_module_info(process_handle, program_counter) {
                get_function_location_in_module(
                    &dbghlp,
                    &module_info,
                    process_handle,
                    program_counter,
                    frame.InlineFrameContext,
                )
            } else {
                // We ignore the error from sym_get_module_info because corrupt stacks in the
                // target are a common cause of not finding the module - a condition we expect.
                get_frame_with_unknown_module(process_handle, program_counter)
            }
        } else {
            get_frame_with_unknown_module(process_handle, program_counter)
        };

        // Avoid pushing consecutive corrupt frames.
        if !debug_stack_frame.is_corrupt_frame()
            || stack
                .last()
                .map_or(true, |f: &DebugStackFrame| !f.is_corrupt_frame())
        {
            stack.push(debug_stack_frame);
        };

        // We want all frames, so continue walking.
        true
    })?;

    Ok(DebugStack::new(stack))
}

#[cfg(test)]
mod test {
    use super::*;

    macro_rules! frame {
        ($name: expr, disp: $disp: expr) => {
            DebugStackFrame::new(
                $name.to_string(),
                DebugFunctionLocation::Offset { disp: $disp },
            )
        };

        ($name: expr, line: ($file: expr, $line: expr)) => {
            DebugStackFrame::new(
                $name.to_string(),
                DebugFunctionLocation::Line {
                    file: $file.to_string(),
                    line: $line,
                },
            )
        };
    }

    #[test]
    fn stable_stack_hash() {
        let frames = vec![
            frame!("ntdll", disp: 88442200),
            frame!("usage", line: ("foo.c", 88)),
            frame!("main", line: ("foo.c", 42)),
        ];
        let stack = DebugStack::new(frames);

        // Hard coded hash constant is what we want to ensure the hash function is stable.
        assert_eq!(stack.stable_hash(), 8083364444338290471);
    }

    #[test]
    fn stable_hash_ignore_jit() {
        let mut frames = vec![
            frame!("ntdll", disp: 88442200),
            frame!("usage", line: ("foo.c", 88)),
            frame!("main", line: ("foo.c", 42)),
        ];

        let base_frames = frames.clone();

        let base_stack = DebugStack::new(base_frames);

        frames.push(DebugStackFrame::corrupt_frame());
        let corrupted_stack = DebugStack::new(frames.clone());

        assert_eq!(corrupted_stack.stable_hash(), base_stack.stable_hash());
    }

    #[test]
    fn stable_hash_assuming_stack_corrupted() {
        let mut frames = vec![
            frame!("ntdll", disp: 88442200),
            frame!("usage", line: ("foo.c", 88)),
            frame!("main", line: ("foo.c", 42)),
        ];

        let base_frames = frames.clone();

        let base_stack = DebugStack::new(base_frames);

        frames.push(frame!(UNKNOWN_MODULE, disp: 1111));
        let corrupted_stack = DebugStack::new(frames.clone());

        assert_eq!(corrupted_stack.stable_hash(), base_stack.stable_hash());
    }

    #[test]
    fn corrupted_top_of_stack() {
        let one_corrupted_frame = vec![frame!(UNKNOWN_MODULE, disp: 88442200)];

        let mut two_corrupted_frames = one_corrupted_frame.clone();
        two_corrupted_frames.push(frame!(UNKNOWN_MODULE, disp: 88442200));

        // If the entire stack is corrupted, we should only hash the top of stack.
        assert_eq!(
            DebugStack::new(two_corrupted_frames).stable_hash(),
            DebugStack::new(one_corrupted_frame).stable_hash()
        );
    }

    #[test]
    fn empty_stack() {
        let stack = DebugStack::new(vec![]);

        assert_eq!(stack.stable_hash(), stack.stable_hash());
    }
}
