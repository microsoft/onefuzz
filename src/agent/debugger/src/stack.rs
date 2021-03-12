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

use crate::dbghelp::{self, DebugHelpGuard, ModuleInfo, SymLineInfo};

const UNKNOWN_MODULE: &str = "<UnknownModule>";

/// The file and line number for frames in the call stack.
#[derive(Clone, Debug, Hash, PartialEq)]
pub struct FileInfo {
    pub file: String,
    pub line: u32,
}

impl Display for FileInfo {
    fn fmt(&self, formatter: &mut Formatter<'_>) -> fmt::Result {
        write!(formatter, "{}:{}", self.file, self.line)
    }
}

impl From<&SymLineInfo> for FileInfo {
    fn from(sym_line_info: &SymLineInfo) -> Self {
        let file = sym_line_info.filename().to_string_lossy().into();
        let line = sym_line_info.line_number();
        FileInfo { file, line }
    }
}

#[derive(Clone, Debug, Hash, PartialEq)]
pub struct DebugFunctionLocation {
    /// File/line if available
    ///
    /// Should be stable - ASLR and JIT should not change source position,
    /// but some precision is lost.
    ///
    /// We mitigate this loss of precision by collecting multiple samples
    /// for the same hash bucket.
    pub file_info: Option<FileInfo>,

    /// Offset if line information not available.
    pub displacement: u64,
}

impl DebugFunctionLocation {
    pub fn new(displacement: u64) -> Self {
        DebugFunctionLocation {
            displacement,
            file_info: None,
        }
    }

    pub fn new_with_file_info(displacement: u64, file_info: FileInfo) -> Self {
        DebugFunctionLocation {
            displacement,
            file_info: Some(file_info),
        }
    }
}

impl Display for DebugFunctionLocation {
    fn fmt(&self, formatter: &mut Formatter) -> fmt::Result {
        if let Some(file_info) = &self.file_info {
            write!(formatter, "{}", file_info)?;
        } else {
            write!(formatter, "0x{:x}", self.displacement)?;
        }
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
            DebugStackFrame::Frame { function, location } => {
                if let Some(file_info) = &location.file_info {
                    write!(formatter, "{} {}", function, file_info)
                } else {
                    write!(formatter, "{}+0x{:x}", function, location.displacement)
                }
            }
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
    pub frames: Vec<DebugStackFrame>,
}

impl DebugStack {
    pub fn new(frames: Vec<DebugStackFrame>) -> DebugStack {
        DebugStack { frames }
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
        for frame in &self.frames {
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

        let displacement = sym_info.displacement();
        let location = match sym_line_info {
            // Don't use file/line for these magic line numbers.
            Ok(ref sym_line_info) if !sym_line_info.is_fake_line_number() => {
                DebugFunctionLocation::new_with_file_info(displacement, sym_line_info.into())
            }

            _ => DebugFunctionLocation::new(displacement),
        };

        DebugStackFrame::new(function, location)
    } else {
        // No function - assume we have an exe with no pdb (so no exports). This should be
        // common, so we won't report an error. We do want a nice(ish) location though.
        let location = DebugFunctionLocation::new(program_counter - module_info.base_address());
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

                let location = DebugFunctionLocation::new(offset);
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

    dbghlp.stackwalk_ex(process_handle, thread_handle, |frame| {
        let program_counter = frame.AddrPC.Offset;

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
            DebugStackFrame::new($name.to_string(), DebugFunctionLocation::new($disp))
        };

        ($name: expr, disp: $disp: expr, line: ($file: expr, $line: expr)) => {
            DebugStackFrame::new(
                $name.to_string(),
                DebugFunctionLocation::new_with_file_info(
                    $disp,
                    FileInfo {
                        file: $file.to_string(),
                        line: $line,
                    },
                ),
            )
        };
    }

    #[test]
    fn stable_stack_hash() {
        let frames = vec![
            frame!("ntdll", disp: 88442200),
            frame!("usage", disp: 10, line: ("foo.c", 88)),
            frame!("main", disp: 20, line: ("foo.c", 42)),
        ];
        let stack = DebugStack::new(frames);

        // Hard coded hash constant is what we want to ensure
        // the hash function is relatively stable.
        assert_eq!(stack.stable_hash(), 4643290346391834992);
    }

    #[test]
    fn stable_hash_ignore_jit() {
        let mut frames = vec![
            frame!("ntdll", disp: 88442200),
            frame!("usage", disp: 10, line: ("foo.c", 88)),
            frame!("main", disp: 20, line: ("foo.c", 42)),
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
            frame!("usage", disp: 10, line: ("foo.c", 88)),
            frame!("main", disp: 20, line: ("foo.c", 42)),
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
