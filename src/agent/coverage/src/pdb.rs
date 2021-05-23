// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::{
    ffi::CStr,
    fs,
    path::{Path, PathBuf},
};

use anyhow::Result;
use debugger::dbghelp::DebugHelpGuard;
use goblin::pe::{debug::DebugData, PE};
use winapi::um::{dbghelp::SYMOPT_EXACT_SYMBOLS, winnt::HANDLE};

// This is a fallback pseudo-handle used for interacting with dbghelp.
//
// We want to avoid `(HANDLE) -1`, because that pseudo-handle is reserved for
// the current process. Reusing it is documented as causing unexpected dbghelp
// behavior when debugging other processes (which we typically will be).
//
// By picking some other very large value, we avoid collisions with handles that
// are concretely either table indices or virtual addresses.
//
// See: https://docs.microsoft.com/en-us/windows/win32/api/dbghelp/nf-dbghelp-syminitializew
const PSEUDO_HANDLE: HANDLE = -2i64 as _;

pub fn find_pdb_path(
    pe_path: &Path,
    pe: &PE,
    target_handle: Option<HANDLE>,
) -> Result<Option<PathBuf>> {
    let cv = if let Some(DebugData {
        image_debug_directory: _,
        codeview_pdb70_debug_info: Some(cv),
    }) = pe.debug_data
    {
        cv
    } else {
        anyhow::bail!("PE missing Codeview PDB debug info: {}", pe_path.display(),)
    };

    let cv_filename = CStr::from_bytes_with_nul(cv.filename)?.to_str()?;

    // This field is named `filename`, but it may be an absolute path.
    // The callee `find_pdb_file_in_path()` handles either.
    let cv_filename = Path::new(cv_filename);

    // If the PE-specified PDB file exists on disk, use that.
    if let Ok(metadata) = fs::metadata(&cv_filename) {
        if metadata.is_file() {
            return Ok(Some(cv_filename.to_owned()));
        }
    }

    // If we have one, use the the process handle for an existing debug
    let handle = target_handle.unwrap_or(PSEUDO_HANDLE);

    let dbghelp = debugger::dbghelp::lock()?;

    // If a target handle was provided, we assume the caller initialized the
    // dbghelp symbol handler, and will clean up after itself.
    //
    // Otherwise, initialize a symbol handler with our own pseudo-path, and use
    // a drop guard to ensure we don't leak resources.
    let _cleanup = if target_handle.is_some() {
        None
    } else {
        dbghelp.sym_initialize(handle)?;
        Some(DbgHelpCleanupGuard::new(&dbghelp, handle))
    };

    // Enable signature and age checking.
    let options = dbghelp.sym_get_options();
    dbghelp.sym_set_options(options | SYMOPT_EXACT_SYMBOLS);

    let mut search_path = dbghelp.sym_get_search_path(handle)?;

    log::debug!("initial search path = {:?}", search_path);

    // Try to add the directory of the PE to the PDB search path.
    //
    // This may be redundant, and should always succeed.
    if let Some(pe_dir) = pe_path.parent() {
        log::debug!("pushing PE dir to search path = {:?}", pe_dir.display());

        search_path.push(";");
        search_path.push(pe_dir);
    } else {
        log::warn!("PE path has no parent dir: {}", pe_path.display());
    }

    dbghelp.sym_set_search_path(handle, search_path)?;

    let pdb_path =
        dbghelp.find_pdb_file_in_path(handle, cv_filename, cv.codeview_signature, cv.age)?;

    Ok(pdb_path)
}

/// On drop, deallocates all resources associated with its process handle.
struct DbgHelpCleanupGuard<'d> {
    dbghelp: &'d DebugHelpGuard,
    process_handle: HANDLE,
}

impl<'d> DbgHelpCleanupGuard<'d> {
    pub fn new(dbghelp: &'d DebugHelpGuard, process_handle: HANDLE) -> Self {
        Self {
            dbghelp,
            process_handle,
        }
    }
}

impl<'d> Drop for DbgHelpCleanupGuard<'d> {
    fn drop(&mut self) {
        if let Err(err) = self.dbghelp.sym_cleanup(self.process_handle) {
            log::error!("error cleaning up symbol handler: {:?}", err);
        }
    }
}
