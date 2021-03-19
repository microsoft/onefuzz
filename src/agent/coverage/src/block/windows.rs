// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::BTreeMap;
use std::process::Command;
use std::time::{Duration, Instant};

use anyhow::Result;
use debugger::{
    debugger::{BreakpointId, BreakpointType, DebugEventHandler, Debugger},
    target::Module,
};

use crate::block::CommandBlockCov;
use crate::cache::ModuleCache;
use crate::code::ModulePath;

pub fn record(cmd: Command) -> Result<CommandBlockCov> {
    let timeout = Duration::from_secs(5);
    let mut recorder = Recorder::new(timeout);
    recorder.record(cmd)?;
    Ok(recorder.coverage)
}

#[derive(Clone, Debug)]
pub struct Recorder {
    breakpoints: Breakpoints,
    cache: ModuleCache,
    coverage: CommandBlockCov,
    started: Instant,
    timed_out: bool,
    timeout: Duration,
}

impl Recorder {
    pub fn new(timeout: Duration) -> Self {
        let breakpoints = Breakpoints::default();
        let cache = ModuleCache::default();
        let coverage = CommandBlockCov::default();
        let started = Instant::now();
        let timed_out = false;

        Self {
            breakpoints,
            cache,
            coverage,
            started,
            timed_out,
            timeout,
        }
    }

    pub fn record(&mut self, cmd: Command) -> Result<()> {
        let (mut dbg, _child) = Debugger::init(cmd, self)?;
        dbg.run(self)?;
        Ok(())
    }

    fn handle_on_create_process(&mut self, dbg: &mut Debugger, module: &Module) -> Result<()> {
        log::debug!("process created: {}", module.path().display());

        if let Err(err) = dbg.target().sym_initialize() {
            log::error!(
                "unable to initialize symbol handler for new process {}: {:?}",
                module.path().display(),
                err,
            );
        }

        self.insert_module(dbg, module)
    }

    fn handle_on_load_dll(&mut self, dbg: &mut Debugger, module: &Module) -> Result<()> {
        log::debug!("DLL loaded: {}", module.path().display());

        self.insert_module(dbg, module)
    }

    fn handle_on_breakpoint(&mut self, dbg: &mut Debugger, id: BreakpointId) -> Result<()> {
        if let Some(breakpoint) = self.breakpoints.get(id) {
            if log::max_level() == log::Level::Trace {
                use iced_x86::Register::RIP;

                let name = breakpoint.module.name().to_string_lossy();
                let offset = breakpoint.offset;
                let pc = dbg.read_register_u64(RIP)?;

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
                .increment(breakpoint.module, breakpoint.offset)?;
        } else {
            log::error!("hit breakpoint without data: {}", id.0);
        }

        Ok(())
    }

    fn handle_on_poll(&mut self, dbg: &mut Debugger) {
        if !self.timed_out && self.started.elapsed() > self.timeout {
            self.timed_out = true;
            dbg.quit_debugging();
        }
    }

    fn insert_module(&mut self, dbg: &mut Debugger, module: &Module) -> Result<()> {
        let path = ModulePath::new(module.path().to_owned())?;

        match self.cache.fetch(&path) {
            Ok(Some(info)) => {
                let new = self.coverage.insert(&path, info.blocks.iter().copied());

                if !new {
                    return Ok(());
                }

                self.breakpoints
                    .set(dbg, module, info.blocks.iter().copied())?;

                log::debug!("set {} breakpoints for module {}", info.blocks.len(), path);
            }
            Ok(None) => {
                log::warn!("could not find module: {}", path);
            }
            Err(err) => {
                log::warn!("could not disassemble module {}: {:?}", path, err);
            }
        }

        Ok(())
    }

    fn stop(&self, dbg: &mut Debugger) {
        dbg.quit_debugging();
    }
}

impl DebugEventHandler for Recorder {
    fn on_create_process(&mut self, dbg: &mut Debugger, module: &Module) {
        if self.handle_on_create_process(dbg, module).is_err() {
            self.stop(dbg);
        }
    }

    fn on_load_dll(&mut self, dbg: &mut Debugger, module: &Module) {
        if self.handle_on_load_dll(dbg, module).is_err() {
            self.stop(dbg);
        }
    }

    fn on_breakpoint(&mut self, dbg: &mut Debugger, bp: BreakpointId) {
        if self.handle_on_breakpoint(dbg, bp).is_err() {
            self.stop(dbg);
        }
    }

    fn on_poll(&mut self, dbg: &mut Debugger) {
        self.handle_on_poll(dbg);
    }
}

#[derive(Clone, Debug, Default)]
struct Breakpoints {
    modules: Vec<ModulePath>,
    registered: BTreeMap<BreakpointId, (usize, u64)>,
}

impl Breakpoints {
    pub fn get(&self, id: BreakpointId) -> Option<BreakpointData> {
        let (module_index, offset) = self.registered.get(&id).copied()?;
        let module = self.modules.get(module_index)?;
        Some(BreakpointData { module, offset })
    }

    pub fn set(
        &mut self,
        dbg: &mut Debugger,
        module: &Module,
        offsets: impl Iterator<Item = u64>,
    ) -> Result<()> {
        // From the `target::Module`, create and save a `ModulePath`.
        let module_path = ModulePath::new(module.path().to_owned())?;
        let module_index = self.modules.len();
        self.modules.push(module_path);

        for offset in offsets {
            // Register the breakpoint in the running target address space.
            let id = dbg.register_breakpoint(module.name(), offset, BreakpointType::OneTime);

            // Associate the opaque `BreakpointId` with the module and offset.
            self.registered.insert(id, (module_index, offset));
        }

        log::debug!("{} total registered modules", self.modules.len());
        log::debug!("{} total registered breakpoints", self.registered.len());

        Ok(())
    }
}

#[derive(Clone, Copy, Debug)]
pub struct BreakpointData<'a> {
    pub module: &'a ModulePath,
    pub offset: u64,
}
