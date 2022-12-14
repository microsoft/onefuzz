// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::BTreeMap;
use std::path::Path;
use std::process::Command;
use std::time::Duration;

use anyhow::{anyhow, Result};
use debuggable_module::load_module::LoadModule;
use debuggable_module::loader::Loader;
use debuggable_module::path::FilePath;
use debuggable_module::windows::WindowsModule;
use debuggable_module::Offset;
use debugger::{BreakpointId, BreakpointType, DebugEventHandler, Debugger, ModuleLoadInfo};

use crate::allowlist::TargetAllowList;
use crate::binary::{self, BinaryCoverage};

pub fn record(
    cmd: Command,
    timeout: Duration,
    allowlist: impl Into<Option<TargetAllowList>>,
) -> Result<BinaryCoverage> {
    let loader = Loader::new();
    let allowlist = allowlist.into().unwrap_or_default();

    crate::timer::timed(timeout, move || {
        let mut recorder = WindowsRecorder::new(&loader, allowlist);
        let (mut dbg, _child) = Debugger::init(cmd, &mut recorder)?;
        dbg.run(&mut recorder)?;

        Ok(recorder.coverage)
    })?
}

pub struct WindowsRecorder<'data> {
    allowlist: TargetAllowList,
    breakpoints: Breakpoints,
    coverage: BinaryCoverage,
    loader: &'data Loader,
    modules: BTreeMap<FilePath, WindowsModule<'data>>,
}

impl<'data> WindowsRecorder<'data> {
    pub fn new(loader: &'data Loader, allowlist: TargetAllowList) -> Self {
        let breakpoints = Breakpoints::default();
        let coverage = BinaryCoverage::default();
        let modules = BTreeMap::new();

        Self {
            allowlist,
            breakpoints,
            coverage,
            loader,
            modules,
        }
    }

    pub fn allowlist(&self) -> &TargetAllowList {
        &self.allowlist
    }

    pub fn allowlist_mut(&mut self) -> &mut TargetAllowList {
        &mut self.allowlist
    }

    fn try_on_create_process(&mut self, dbg: &mut Debugger, module: &ModuleLoadInfo) -> Result<()> {
        // Not necessary for PDB search, but enables use of other `dbghelp` APIs.
        if let Err(err) = dbg.target().maybe_sym_initialize() {
            error!(
                "unable to initialize symbol handler for new process {}: {:?}",
                module.path().display(),
                err,
            );
        }

        self.insert_module(dbg, module)
    }

    fn try_on_load_dll(&mut self, dbg: &mut Debugger, module: &ModuleLoadInfo) -> Result<()> {
        self.insert_module(dbg, module)
    }

    fn try_on_breakpoint(&mut self, _dbg: &mut Debugger, id: BreakpointId) -> Result<()> {
        let breakpoint = self
            .breakpoints
            .remove(id)
            .ok_or_else(|| anyhow!("stopped on dangling breakpoint"))?;

        let coverage = self
            .coverage
            .modules
            .get_mut(&breakpoint.module)
            .ok_or_else(|| anyhow!("coverage not initialized for module: {}", breakpoint.module))?;

        coverage.increment(breakpoint.offset)?;

        Ok(())
    }

    fn stop(&self, dbg: &mut Debugger) {
        dbg.quit_debugging();
    }

    fn insert_module(&mut self, dbg: &mut Debugger, module: &ModuleLoadInfo) -> Result<()> {
        let path = FilePath::new(module.path().to_string_lossy())?;

        if !self.allowlist.modules.is_allowed(&path) {
            debug!("not inserting denylisted module: {path}");
            return Ok(());
        }

        let module = if let Ok(m) = WindowsModule::load(&self.loader, path.clone()) {
            m
        } else {
            debug!("skipping undebuggable module: {path}");
            return Ok(());
        };

        let coverage = binary::find_coverage_sites(&module, &self.allowlist)?;

        for offset in coverage.as_ref().keys().copied() {
            let breakpoint = Breakpoint::new(path.clone(), offset);
            self.breakpoints.set(dbg, breakpoint)?;
        }

        self.coverage.modules.insert(path.clone(), coverage);

        self.modules.insert(path.clone(), module);

        Ok(())
    }
}

#[derive(Debug, Default)]
struct Breakpoints {
    id_to_offset: BTreeMap<BreakpointId, Offset>,
    offset_to_breakpoint: BTreeMap<Offset, Breakpoint>,
}

impl Breakpoints {
    pub fn set(&mut self, dbg: &mut Debugger, breakpoint: Breakpoint) -> Result<()> {
        if self.is_set(&breakpoint) {
            return Ok(());
        }

        self.write(dbg, breakpoint)
    }

    // Unguarded action that ovewrites both the target process address space and our index
    // of known breakpoints. Callers must use `set()`, which avoids redundant breakpoint
    // setting.
    fn write(&mut self, dbg: &mut Debugger, breakpoint: Breakpoint) -> Result<()> {
        // The `debugger` crates tracks loaded modules by base name. If a path or file
        // name is used, software breakpoints will not be written.
        let name = Path::new(breakpoint.module.base_name());
        let id = dbg.new_rva_breakpoint(name, breakpoint.offset.0, BreakpointType::OneTime)?;

        self.id_to_offset.insert(id, breakpoint.offset);
        self.offset_to_breakpoint
            .insert(breakpoint.offset, breakpoint);

        Ok(())
    }

    pub fn is_set(&self, breakpoint: &Breakpoint) -> bool {
        self.offset_to_breakpoint.contains_key(&breakpoint.offset)
    }

    pub fn remove(&mut self, id: BreakpointId) -> Option<Breakpoint> {
        let offset = self.id_to_offset.remove(&id)?;
        self.offset_to_breakpoint.remove(&offset)
    }
}

#[derive(Clone, Debug)]
struct Breakpoint {
    module: FilePath,
    offset: Offset,
}

impl Breakpoint {
    pub fn new(module: FilePath, offset: Offset) -> Self {
        Self { module, offset }
    }
}

impl<'data> DebugEventHandler for WindowsRecorder<'data> {
    fn on_create_process(&mut self, dbg: &mut Debugger, module: &ModuleLoadInfo) {
        if let Err(err) = self.try_on_create_process(dbg, module) {
            warn!("{err}");
            self.stop(dbg);
        }
    }

    fn on_load_dll(&mut self, dbg: &mut Debugger, module: &ModuleLoadInfo) {
        if let Err(err) = self.try_on_load_dll(dbg, module) {
            warn!("{err}");
            self.stop(dbg);
        }
    }

    fn on_breakpoint(&mut self, dbg: &mut Debugger, bp: BreakpointId) {
        if let Err(err) = self.try_on_breakpoint(dbg, bp) {
            warn!("{err}");
            self.stop(dbg);
        }
    }
}
