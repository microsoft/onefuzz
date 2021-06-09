// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::BTreeMap;
use std::process::Command;
use std::time::{Duration, Instant};

use anyhow::{Context, Result};
use debugger::{BreakpointId, BreakpointType, DebugEventHandler, Debugger, ModuleLoadInfo};

use crate::block::CommandBlockCov;
use crate::cache::ModuleCache;
use crate::code::{CmdFilter, ModulePath};

pub fn record(cmd: Command, filter: CmdFilter, timeout: Duration) -> Result<CommandBlockCov> {
    let mut cache = ModuleCache::default();
    let mut recorder = Recorder::new(&mut cache, filter);
    let mut handler = RecorderEventHandler::new(&mut recorder, timeout);
    handler.run(cmd)?;
    Ok(recorder.into_coverage())
}

#[derive(Debug)]
pub struct RecorderEventHandler<'r, 'c> {
    recorder: &'r mut Recorder<'c>,
    started: Instant,
    timed_out: bool,
    timeout: Duration,
}

impl<'r, 'c> RecorderEventHandler<'r, 'c> {
    pub fn new(recorder: &'r mut Recorder<'c>, timeout: Duration) -> Self {
        let started = Instant::now();
        let timed_out = false;

        Self {
            recorder,
            started,
            timed_out,
            timeout,
        }
    }

    pub fn time_out(&self) -> bool {
        self.timed_out
    }

    pub fn timeout(&self) -> Duration {
        self.timeout
    }

    pub fn run(&mut self, cmd: Command) -> Result<()> {
        let (mut dbg, _child) = Debugger::init(cmd, self).context("initializing debugger")?;
        dbg.run(self).context("running debuggee")?;
        Ok(())
    }

    fn on_poll(&mut self, dbg: &mut Debugger) {
        if !self.timed_out && self.started.elapsed() > self.timeout {
            self.timed_out = true;
            dbg.quit_debugging();
        }
    }

    fn stop(&self, dbg: &mut Debugger) {
        dbg.quit_debugging();
    }
}

#[derive(Debug)]
pub struct Recorder<'c> {
    breakpoints: Breakpoints,

    // Reference to allow in-memory reuse across runs.
    cache: &'c mut ModuleCache,

    // Note: this could also be a reference to enable reuse across runs, to
    // support implicit calculation of total coverage for a corpus. For now,
    // assume callers will merge this into a separate struct when needed.
    coverage: CommandBlockCov,

    filter: CmdFilter,
}

impl<'c> Recorder<'c> {
    pub fn new(cache: &'c mut ModuleCache, filter: CmdFilter) -> Self {
        let breakpoints = Breakpoints::default();
        let coverage = CommandBlockCov::default();

        Self {
            breakpoints,
            cache,
            coverage,
            filter,
        }
    }

    pub fn coverage(&self) -> &CommandBlockCov {
        &self.coverage
    }

    pub fn into_coverage(self) -> CommandBlockCov {
        self.coverage
    }

    pub fn on_create_process(&mut self, dbg: &mut Debugger, module: &ModuleLoadInfo) -> Result<()> {
        log::debug!("process created: {}", module.path().display());

        // Not necessary for PDB search, but enables use of other `dbghelp` APIs.
        if let Err(err) = dbg.target().maybe_sym_initialize() {
            log::error!(
                "unable to initialize symbol handler for new process {}: {:?}",
                module.path().display(),
                err,
            );
        }

        self.insert_module(dbg, module)
    }

    pub fn on_load_dll(&mut self, dbg: &mut Debugger, module: &ModuleLoadInfo) -> Result<()> {
        log::debug!("DLL loaded: {}", module.path().display());

        self.insert_module(dbg, module)
    }

    pub fn on_breakpoint(&mut self, dbg: &mut Debugger, id: BreakpointId) -> Result<()> {
        if let Some(breakpoint) = self.breakpoints.get(id) {
            if log::max_level() == log::Level::Trace {
                let name = breakpoint.module.name().to_string_lossy();
                let offset = breakpoint.offset;
                let pc = dbg.read_program_counter().context("reading PC on breakpoint")?;

                if let Ok(sym) = dbg.get_symbol(pc) {
                    log::trace!(
                        "{:>16x}: {}+{:x} ({}+{:x})",
                        pc,
                        name,
                        offset,
                        sym.symbol(),
                        sym.displacement(),
                    );
                } else {
                    log::trace!("{:>16x}: {}+{:x}", pc, name, offset);
                }
            }

            self.coverage
                .increment(breakpoint.module, breakpoint.offset);
        } else {
            let pc = if let Ok(pc) = dbg.read_program_counter() {
                format!("{:x}", pc)
            } else {
                "???".into()
            };

            log::error!("hit breakpoint without data, id = {}, pc = {}", id.0, pc);
        }

        Ok(())
    }

    fn insert_module(&mut self, dbg: &mut Debugger, module: &ModuleLoadInfo) -> Result<()> {
        let path = ModulePath::new(module.path().to_owned()).context("parsing module path")?;

        if !self.filter.includes_module(&path) {
            log::debug!("skipping module: {}", path);
            return Ok(());
        }

        // Do not pass the debuggee's actual process handle here. Any passed handle is
        // used as the symbol handler context within the cache's PDB search. Instead, use
        // the default internal pseudo-handle for "static" `dbghelp` usage. This lets us
        // query `dbghelp` immediately upon observing the `CREATE_PROCESS_DEBUG_EVENT`,
        // before we would be able to for a running debuggee.
        match self.cache.fetch(&path, None) {
            Ok(Some(info)) => {
                let new = self.coverage.insert(&path, info.blocks.iter().copied());

                if !new {
                    return Ok(());
                }

                self.breakpoints
                    .set(dbg, module, info.blocks.iter().copied()).context("setting breakpoints for module")?;

                log::debug!("set {} breakpoints for module {}", info.blocks.len(), path);
            }
            Ok(None) => {
                log::debug!("could not find module: {}", path);
            }
            Err(err) => {
                log::debug!("could not disassemble module {}: {:?}", path, err);
            }
        }

        Ok(())
    }
}

impl<'r, 'c> DebugEventHandler for RecorderEventHandler<'r, 'c> {
    fn on_create_process(&mut self, dbg: &mut Debugger, module: &ModuleLoadInfo) {
        if self.recorder.on_create_process(dbg, module).is_err() {
            self.stop(dbg);
        }
    }

    fn on_load_dll(&mut self, dbg: &mut Debugger, module: &ModuleLoadInfo) {
        if self.recorder.on_load_dll(dbg, module).is_err() {
            self.stop(dbg);
        }
    }

    fn on_breakpoint(&mut self, dbg: &mut Debugger, bp: BreakpointId) {
        if self.recorder.on_breakpoint(dbg, bp).is_err() {
            self.stop(dbg);
        }
    }

    fn on_poll(&mut self, dbg: &mut Debugger) {
        self.on_poll(dbg);
    }
}

/// Relates opaque, runtime-generated breakpoint IDs to their corresponding
/// location, via module and offset.
#[derive(Clone, Debug, Default)]
struct Breakpoints {
    // Breakpoint-associated module paths, referenced by index to save space and
    // avoid copying.
    modules: Vec<ModulePath>,

    // Map of breakpoint IDs to data which pick out an code location. For a
    // value `(module, offset)`, `module` is an index into `self.modules`, and
    // `offset` is a VA offset relative to the module base.
    registered: BTreeMap<BreakpointId, (usize, u32)>,
}

impl Breakpoints {
    pub fn get(&self, id: BreakpointId) -> Option<BreakpointData<'_>> {
        let (module_index, offset) = self.registered.get(&id).copied().context("looking up breakpoint")?;
        let module = self.modules.get(module_index)?;
        Some(BreakpointData { module, offset })
    }

    pub fn set(
        &mut self,
        dbg: &mut Debugger,
        module: &ModuleLoadInfo,
        offsets: impl Iterator<Item = u32>,
    ) -> Result<()> {
        // From the `debugger::ModuleLoadInfo`, create and save a `ModulePath`.
        let module_path = ModulePath::new(module.path().to_owned())?;
        let module_index = self.modules.len();
        self.modules.push(module_path);

        for offset in offsets {
            // Register the breakpoint in the running target address space.
            let id =
                dbg.new_rva_breakpoint(module.name(), offset as u64, BreakpointType::OneTime)?;

            // Associate the opaque `BreakpointId` with the module and offset.
            self.registered.insert(id, (module_index, offset));
        }

        log::debug!("{} total registered modules", self.modules.len());
        log::debug!("{} total registered breakpoints", self.registered.len());

        Ok(())
    }
}

/// Code location data associated with an opaque breakpoint ID.
#[derive(Clone, Copy, Debug)]
pub struct BreakpointData<'a> {
    pub module: &'a ModulePath,
    pub offset: u32,
}
