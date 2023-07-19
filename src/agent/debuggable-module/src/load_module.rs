// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::io::Cursor;

use anyhow::{bail, Result};
use goblin::Hint;

use crate::linux::LinuxModule;
use crate::loader::Loader;
use crate::path::FilePath;
use crate::windows::WindowsModule;
use crate::Module;

pub trait LoadModule<'data>
where
    Self: Sized,
{
    fn load(loader: &'data Loader, exe_path: FilePath) -> Result<Self>;
}

impl<'data> LoadModule<'data> for LinuxModule<'data> {
    fn load(loader: &'data Loader, elf_path: FilePath) -> Result<Self> {
        let data = loader.load(&elf_path)?;
        LinuxModule::new(elf_path, data)
    }
}

impl<'data> LoadModule<'data> for WindowsModule<'data> {
    fn load(loader: &'data Loader, pe_path: FilePath) -> Result<Self> {
        let pdb_path = find_pdb(&pe_path)?;
        let pdb_data = loader.load(&pdb_path)?;
        let pe_data = loader.load(&pe_path)?;

        WindowsModule::new(pe_path, pe_data, pdb_path, pdb_data)
    }
}

impl<'data> LoadModule<'data> for Box<dyn Module<'data> + 'data> {
    fn load(loader: &'data Loader, exe_path: FilePath) -> Result<Self> {
        let exe_data = loader.load(&exe_path)?;

        let mut cursor = Cursor::new(&exe_data);
        let hint = goblin::peek(&mut cursor)?;

        let module: Box<dyn Module<'data>> = match hint {
            Hint::Elf(..) => {
                let module = LinuxModule::load(loader, exe_path)?;
                Box::new(module)
            }
            Hint::PE => {
                let module = WindowsModule::load(loader, exe_path)?;
                Box::new(module)
            }
            _ => {
                bail!("unknown module file format: {:x?}", hint);
            }
        };

        Ok(module)
    }
}

fn find_pdb(pe_path: &FilePath) -> Result<FilePath> {
    // Check if the PDB is in the same dir as the PE.
    let same_dir_path = pe_path.with_extension("pdb");

    if same_dir_path.as_path().exists() {
        return FilePath::new(same_dir_path);
    }

    bail!("could not find PDB for PE `{pe_path}`");
}
