// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::BTreeMap;
use std::path::Path;

use anyhow::{anyhow, bail, Error, Result};
use debuggable_module::debuginfo::{DebugInfo, Function};
use debuggable_module::load_module::LoadModule;
use debuggable_module::loader::Loader;
use debuggable_module::path::FilePath;
use debuggable_module::windows::WindowsModule;
use debuggable_module::{Module, Offset};
use debugger::{BreakpointId, BreakpointType, DebugEventHandler, Debugger, ModuleLoadInfo};

use crate::allowlist::TargetAllowList;
use crate::binary::{self, BinaryCoverage};

pub struct WindowsRecorder<'data> {
    allowlist: TargetAllowList,
    breakpoints: Breakpoints,
    deferred_breakpoints: BTreeMap<BreakpointId, (Breakpoint, DeferralState)>,
    pub coverage: BinaryCoverage,
    loader: &'data Loader,
    modules: BTreeMap<FilePath, (WindowsModule<'data>, DebugInfo)>,
    pub stop_error: Option<Error>,
}

impl<'data> WindowsRecorder<'data> {
    pub fn new(loader: &'data Loader, allowlist: TargetAllowList) -> Self {
        let breakpoints = Breakpoints::default();
        let deferred_breakpoints = BTreeMap::new();
        let coverage = BinaryCoverage::default();
        let modules = BTreeMap::new();
        let stop_error = None;

        Self {
            allowlist,
            breakpoints,
            deferred_breakpoints,
            coverage,
            loader,
            modules,
            stop_error,
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

    fn try_on_breakpoint(&mut self, dbg: &mut Debugger, id: BreakpointId) -> Result<()> {
        let pc = dbg.read_program_counter()?;
        let tid = dbg.get_current_thread_id();

        if let Some((trigger, state)) = self.deferred_breakpoints.remove(&id) {
            match state {
                DeferralState::NotEntered => {
                    // Find the return address.
                    let frame = dbg.get_current_frame()?;
                    let ret = frame.return_address();
                    let id = dbg.new_address_breakpoint(ret, BreakpointType::OneTime)?;

                    // Update the state for this deferral to set module coverage breakpoints on ret.
                    let thread_id = dbg.get_current_thread_id();
                    let state = DeferralState::PendingReturn { thread_id };
                    self.deferred_breakpoints.insert(id, (trigger, state));
                    // return Ok(());
                },
                DeferralState::PendingReturn { thread_id } => {
                    if dbg.get_current_thread_id() == thread_id {
                        // We've returned from the trigger function, and on the same thread.
                        //
                        // It's safe to set coverage breakpoints.
                        self.set_module_breakpoints(dbg, trigger.module)?;
                    } else {
                        // Hit a ret breakpoint, but on the wrong thread. Reset it so the correct
                        // thread has a chance to see it.
                        //
                        // We only defer breakpoints in image initialization code, so we don't
                        // expect to reach this code in practice.
                        let id = trigger.set(dbg)?;
                        self.deferred_breakpoints.insert(id, (trigger, state));
                        // return Ok(());
                    }
                },
            }

            return Ok(());
        }

        let breakpoint = self
            .breakpoints
            .remove(id);

        let Some(breakpoint) = breakpoint else {
            let stack = dbg.get_current_stack()?;
            bail!("stopped on dangling breakpoint, debuggee stack:\n{}", stack);
        };

        let coverage = self
            .coverage
            .modules
            .get_mut(&breakpoint.module)
            .ok_or_else(|| anyhow!("coverage not initialized for module: {}", breakpoint.module))?;

        coverage.increment(breakpoint.offset);

        Ok(())
    }

    fn stop(&mut self, dbg: &mut Debugger, stop_error: impl Into<Option<Error>>) {
        self.stop_error = stop_error.into();
        dbg.quit_debugging();
    }

    fn insert_module(&mut self, dbg: &mut Debugger, module: &ModuleLoadInfo) -> Result<()> {
        let path = FilePath::new(module.path().to_string_lossy())?;

        if !self.allowlist.modules.is_allowed(&path) {
            debug!("not inserting denylisted module: {path}");
            return Ok(());
        }

        let module = if let Ok(m) = WindowsModule::load(self.loader, path.clone()) {
            m
        } else {
            debug!("skipping undebuggable module: {path}");
            return Ok(());
        };

        let debuginfo = module.debuginfo()?;
        self.modules.insert(path.clone(), (module, debuginfo));

        self.set_or_defer_module_breakpoints(dbg, path)?;

        Ok(())
    }

    fn set_or_defer_module_breakpoints(&mut self, dbg: &mut Debugger, path: FilePath) -> Result<()> {
        let (_module, debuginfo) = &self.modules[&path];

        // For borrocwk.
        let mut trigger = None;

        for function in debuginfo.functions() {
            // Called on process startup.
            if function.name.contains("__asan::AsanInitInternal") {
                trigger = Some(function.clone());
                break;
            }

            // Called on shared library load.
            if function.name.contains("DllMain(") {
                trigger = Some(function.clone());
                break;
            }
        }

        if let Some(trigger) = trigger {
            debug!("deferring module breakpoints for {}", path);
            self.defer_module_breakpoints(dbg, path, trigger)
        } else {
            debug!("immediately setting module breakpoints for {}", path);
            self.set_module_breakpoints(dbg, path)
        }
    }

    fn defer_module_breakpoints(
        &mut self,
        dbg: &mut Debugger,
        path: FilePath,
        trigger: Function,
    ) -> Result<()> {
        let state = DeferralState::NotEntered;
        let entry_breakpoint = Breakpoint::new(path, trigger.offset);
        let id = entry_breakpoint.set(dbg)?;
        let thread_id = dbg.get_current_thread_id();

        self.deferred_breakpoints.insert(id, (entry_breakpoint, state));

        Ok(())
    }

    fn set_module_breakpoints(&mut self, dbg: &mut Debugger, path: FilePath) -> Result<()> {
        let (module, _) = &self.modules[&path];
        let coverage = binary::find_coverage_sites(module, &self.allowlist)?;

        for offset in coverage.as_ref().keys().copied() {
            let breakpoint = Breakpoint::new(path.clone(), offset);
            self.breakpoints.set(dbg, breakpoint)?;
        }

        let count = coverage.offsets.len();
        debug!("set {} breakpoints for module {}", count, path);

        self.coverage.modules.insert(path, coverage);

        Ok(())
    }
}

#[derive(Debug, Default)]
struct Breakpoints {
    id_to_breakpoint: BTreeMap<BreakpointId, Breakpoint>,
}

impl Breakpoints {
    pub fn set(&mut self, dbg: &mut Debugger, breakpoint: Breakpoint) -> Result<()> {
        let id = breakpoint.set(dbg)?;
        self.id_to_breakpoint.insert(id, breakpoint);
        Ok(())
    }

    /// Remove a registered breakpoint from the index.
    ///
    /// This method should be called when a breakpoint is hit to retrieve its associated data.
    /// It does NOT clear the actual breakpoint in the debugger engine.
    pub fn remove(&mut self, id: BreakpointId) -> Option<Breakpoint> {
        self.id_to_breakpoint.remove(&id)
    }
}

#[derive(Clone, Debug)]
struct Breakpoint {
    pub module: FilePath,
    pub offset: Offset,
}

impl Breakpoint {
    pub fn new(module: FilePath, offset: Offset) -> Self {
        Self { module, offset }
    }

    pub fn set(&self, dbg: &mut Debugger) -> Result<BreakpointId> {
        let name = Path::new(self.module.base_name());
        let id = dbg.new_rva_breakpoint(name, self.offset.0, BreakpointType::OneTime)?;
        Ok(id)
    }
}

impl<'data> DebugEventHandler for WindowsRecorder<'data> {
    fn on_create_process(&mut self, dbg: &mut Debugger, module: &ModuleLoadInfo) {
        if let Err(err) = self.try_on_create_process(dbg, module) {
            warn!("{err}");
            self.stop(dbg, err);
        }
    }

    fn on_load_dll(&mut self, dbg: &mut Debugger, module: &ModuleLoadInfo) {
        if let Err(err) = self.try_on_load_dll(dbg, module) {
            warn!("{err}");
            self.stop(dbg, err);
        }
    }

    fn on_breakpoint(&mut self, dbg: &mut Debugger, bp: BreakpointId) {
        if let Err(err) = self.try_on_breakpoint(dbg, bp) {
            warn!("{err}");
            self.stop(dbg, err);
        }
    }
}

enum DeferralState {
    NotEntered,
    PendingReturn { thread_id: u64 },
}
