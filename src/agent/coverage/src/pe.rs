// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![allow(clippy::manual_swap)]

use std::{
    ffi::CStr,
    fs::File,
    path::{Path, PathBuf},
};

use anyhow::Result;
use debugger::dbghelp::DebugHelpGuard;
use fixedbitset::FixedBitSet;
use goblin::pe::{
    debug::{CodeviewPDB70DebugInfo, DebugData},
    PE,
};
use log::trace;
use memmap2::Mmap;
use pdb::{
    AddressMap, FallibleIterator, PdbInternalSectionOffset, ProcedureSymbol, TypeIndex, PDB,
};
use winapi::um::{
    dbghelp::SYMOPT_EXACT_SYMBOLS,
    winnt::{HANDLE, IMAGE_FILE_MACHINE_AMD64, IMAGE_FILE_MACHINE_I386},
};

use crate::intel;

struct JumpTableData {
    pub offset: PdbInternalSectionOffset,
    pub labels: Vec<PdbInternalSectionOffset>,
}

impl JumpTableData {
    pub fn new(offset: PdbInternalSectionOffset) -> Self {
        Self {
            offset,
            labels: vec![],
        }
    }
}

struct ProcSymInfo {
    pub name: String,
    pub offset: PdbInternalSectionOffset,
    pub code_len: u32,
    pub jump_tables: Vec<JumpTableData>,
    pub extra_labels: Vec<PdbInternalSectionOffset>,
}

impl ProcSymInfo {
    pub fn new(
        name: String,
        offset: PdbInternalSectionOffset,
        code_len: u32,
        jump_tables: Vec<JumpTableData>,
        extra_labels: Vec<PdbInternalSectionOffset>,
    ) -> Self {
        Self {
            name,
            offset,
            code_len,
            jump_tables,
            extra_labels,
        }
    }
}

fn offset_within_func(offset: PdbInternalSectionOffset, proc: &ProcedureSymbol) -> bool {
    offset.section == proc.offset.section
        && offset.offset >= proc.offset.offset
        && offset.offset < (proc.offset.offset + proc.len)
}

fn collect_func_sym_info(
    symbols: &mut pdb::SymbolIter<'_>,
    proc: ProcedureSymbol,
) -> Result<ProcSymInfo> {
    let mut jump_tables = vec![];
    let mut extra_labels = vec![];
    while let Some(symbol) = symbols.next()? {
        // Symbols are scoped with `end` marking the last symbol in the scope of the function.
        if symbol.index() == proc.end {
            break;
        }

        match symbol.parse() {
            Ok(pdb::SymbolData::Data(data)) => {
                // Local data *might* be a jump table if it's in the same section as
                // the function. For extra paranoia, we also check that there is no type
                // as that is what VC++ generates. LLVM does not generate debug symbols for
                // jump tables.
                if offset_within_func(data.offset, &proc) && data.type_index == TypeIndex(0) {
                    jump_tables.push(JumpTableData::new(data.offset));
                }
            }
            Ok(pdb::SymbolData::Label(label)) => {
                if offset_within_func(label.offset, &proc) {
                    if let Some(jump_table) = jump_tables.last_mut() {
                        jump_table.labels.push(label.offset);
                    } else {
                        // Maybe not possible to get here, and maybe a bad idea for VC++
                        // because the code length would include this label,
                        // but could be useful if LLVM generates labels but no L_DATA32 record.
                        extra_labels.push(label.offset);
                    }
                }
            }
            Ok(_)
            | Err(pdb::Error::UnimplementedFeature(_))
            | Err(pdb::Error::UnimplementedSymbolKind(_)) => {}
            Err(err) => {
                anyhow::bail!("Error reading symbols: {}", err);
            }
        }
    }

    let result = ProcSymInfo::new(
        proc.name.to_string().to_string(),
        proc.offset,
        proc.len,
        jump_tables,
        extra_labels,
    );
    Ok(result)
}

fn collect_proc_symbols(symbols: &mut pdb::SymbolIter<'_>) -> Result<Vec<ProcSymInfo>> {
    let mut result = vec![];

    while let Some(symbol) = symbols.next()? {
        match symbol.parse() {
            Ok(pdb::SymbolData::Procedure(proc)) => {
                // Collect everything we need for safe disassembly of the function.
                result.push(collect_func_sym_info(symbols, proc)?);
            }
            Ok(_)
            | Err(pdb::Error::UnimplementedFeature(_))
            | Err(pdb::Error::UnimplementedSymbolKind(_)) => {}
            Err(err) => {
                anyhow::bail!("Error reading symbols: {}", err);
            }
        }
    }

    Ok(result)
}

fn find_blocks(
    proc_data: &[ProcSymInfo],
    blocks: &mut FixedBitSet,
    address_map: &AddressMap,
    pe: &PE,
    data: &[u8],
    functions_only: bool,
) -> Result<()> {
    let file_alignment = pe
        .header
        .optional_header
        .unwrap()
        .windows_fields
        .file_alignment;
    let machine = pe.header.coff_header.machine;
    let bitness = match machine {
        IMAGE_FILE_MACHINE_I386 => 32,
        IMAGE_FILE_MACHINE_AMD64 => 64,
        _ => anyhow::bail!("Unsupported architecture {}", machine),
    };

    for proc in proc_data {
        if let Some(rva) = proc.offset.to_rva(address_map) {
            blocks.insert(rva.0 as usize);

            if functions_only {
                continue;
            }

            if let Some(file_offset) =
                goblin::pe::utils::find_offset(rva.0 as usize, &pe.sections, file_alignment)
            {
                // VC++ includes jump tables with the code length which we must exclude
                // from disassembly. We use the minimum address of a jump table since
                // the tables are placed consecutively after the actual code.
                //
                // LLVM 9 **does not** include debug info for jump tables, but conveniently
                // does not include the jump tables in the code length.
                let mut code_len = proc.code_len;

                for table in &proc.jump_tables {
                    if table.offset.section == proc.offset.section
                        && table.offset.offset > proc.offset.offset
                        && (proc.offset.offset + code_len) > table.offset.offset
                    {
                        code_len = table.offset.offset - proc.offset.offset;
                    }

                    for label in &table.labels {
                        if let Some(rva) = label.to_rva(address_map) {
                            blocks.insert(rva.0 as usize)
                        }
                    }
                }

                for label in &proc.extra_labels {
                    if let Some(rva) = label.to_rva(address_map) {
                        blocks.insert(rva.0 as usize)
                    }
                }

                trace!(
                    "analyzing func: {} rva: 0x{:x} file_offset: 0x{:x}",
                    &proc.name,
                    rva.0,
                    file_offset
                );

                intel::find_blocks(
                    bitness,
                    &data[file_offset..file_offset + (code_len as usize)],
                    rva.0,
                    blocks,
                );
            }
        }
    }

    Ok(())
}

pub fn process_module(
    pe_path: impl AsRef<Path>,
    data: &[u8],
    pe: &PE,
    functions_only: bool,
) -> Result<FixedBitSet> {
    if let Some(DebugData {
        image_debug_directory: _,
        codeview_pdb70_debug_info: Some(cv),
    }) = pe.debug_data
    {
        let pdb_path = find_pdb_path(pe_path.as_ref(), &cv)?;
        let pdb_file = File::open(&pdb_path)?;
        let mut pdb = PDB::open(pdb_file)?;

        let address_map = pdb.address_map()?;
        let mut blocks = FixedBitSet::with_capacity(data.len());

        let proc_sym_info = collect_proc_symbols(&mut pdb.global_symbols()?.iter())?;
        find_blocks(
            &proc_sym_info[..],
            &mut blocks,
            &address_map,
            &pe,
            data,
            functions_only,
        )?;

        // Modules in the PDB correspond to object files.
        let dbi = pdb.debug_information()?;
        let mut modules = dbi.modules()?;
        while let Some(module) = modules.next()? {
            if let Some(info) = pdb.module_info(&module)? {
                let proc_sym_info = collect_proc_symbols(&mut info.symbols()?)?;
                find_blocks(
                    &proc_sym_info[..],
                    &mut blocks,
                    &address_map,
                    &pe,
                    data,
                    functions_only,
                )?;
            }
        }

        return Ok(blocks);
    }

    anyhow::bail!("PE missing codeview pdb debug info")
}

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

fn find_pdb_path(pe_path: &Path, cv: &CodeviewPDB70DebugInfo) -> Result<PathBuf> {
    let cv_filename = CStr::from_bytes_with_nul(cv.filename)?.to_str()?;

    // This field is named `filename`, but it may be an absolute path.
    // The callee `find_pdb_file_in_path()` handles either.
    let cv_filename = Path::new(cv_filename);

    // If the PE-specified PDB file exists on disk, use that.
    if std::fs::metadata(&cv_filename)?.is_file() {
        return Ok(cv_filename.to_owned());
    }

    let handle = PSEUDO_HANDLE;

    let dbghelp = debugger::dbghelp::lock()?;
    dbghelp.sym_initialize(handle)?;
    let _cleanup = DbgHelpCleanupGuard::new(&dbghelp, handle);

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

pub fn process_image(path: impl AsRef<Path>, functions_only: bool) -> Result<FixedBitSet> {
    let file = File::open(path.as_ref())?;
    let mmap = unsafe { Mmap::map(&file)? };
    let pe = PE::parse(&mmap)?;

    process_module(path, &mmap, &pe, functions_only)
}
