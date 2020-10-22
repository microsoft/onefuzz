// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::BTreeMap;
use std::process::Command;
use std::time::{Duration, Instant};

use anyhow::Result;
use debugger::{
    dbghelp::SymInfo,
    debugger::{BreakpointId, BreakpointType, Debugger, DebugEventHandler},
    target::Module,
};

use crate::{AppCoverageBlocks, ModuleCoverageBlocks};


pub fn record(cmd: Command) -> Result<AppCoverageBlocks> {
    let mut handler = BlockCoverageHandler::new();

    let (mut dbg, _child) = Debugger::init(cmd, &mut handler)?;
    dbg.run(&mut handler)?;

    Ok(handler.coverage)
}

pub struct BlockCoverageHandler {
    bp_to_block: BTreeMap<BreakpointId, (usize, usize)>,
    coverage: AppCoverageBlocks,
    started: Instant,
    max_duration: Duration,
    timed_out: bool,
}

impl BlockCoverageHandler {
    pub fn new() -> Self {
        let coverage = AppCoverageBlocks::new();
        let bp_to_block = BTreeMap::default();
        let started = Instant::now();
        let max_duration = Duration::from_secs(5);
        let timed_out = false;

        Self { bp_to_block, coverage, max_duration, started, timed_out }
    }

    pub fn pc(&self, dbg: &mut Debugger) -> Result<(u64, Option<SymInfo>)> {
        let rip = iced_x86::Register::RIP;
        let pc = dbg.read_register_u64(rip)?;
        let sym = dbg.get_symbol(pc).ok();

        Ok((pc, sym))
    }

    fn add_module(&mut self, dbg: &mut Debugger, module: &Module) {
        let bitset = crate::pe::process_image(module.path(), false);

        // If we can't add the module, continue debugging.
        // We don't expect to have symbols for every module.
        if let Err(err) = bitset {
            log::warn!(
                "cannot record coverage for module = {}, err = {}",
                module.path().display(),
                err,
            );
            return;
        }

        // Won't panic, due to above check.
        let bitset = bitset.unwrap();
        let module_coverage = ModuleCoverageBlocks::new(module.path(), module.name(), bitset);

        let m = self.coverage.add_module(module_coverage);
        let module_coverage = &self.coverage.modules()[m];
        for (b, block) in module_coverage.blocks().iter().enumerate() {
            let bp = dbg.register_breakpoint(
                module.name(),
                block.rva() as u64,
                BreakpointType::OneTime,
            );
            self.bp_to_block.insert(bp, (m, b));
        }

        log::debug!(
            "inserted {} breakpoints for module {}",
            module_coverage.blocks().len(),
            module.path().display(),
        );
    }

    fn stop(&self, dbg: &mut Debugger) {
        dbg.quit_debugging();
    }

    fn try_on_create_process(&mut self, dbg: &mut Debugger, module: &Module) -> Result<()> {
        dbg.target().sym_initialize()?;

        log::info!("exe loaded: {}, {} bytes",
                 module.path().display(),
                 module.image_size(),
        );

        self.add_module(dbg, module);

        Ok(())
    }

    fn try_on_load_dll(&mut self, dbg: &mut Debugger, module: &Module) -> Result<()> {
        log::info!("dll loaded: {}, {} bytes",
                 module.path().display(),
                 module.image_size(),
        );

        self.add_module(dbg, module);

        Ok(())
    }

    fn try_on_breakpoint(&mut self, dbg: &mut Debugger, bp: BreakpointId) -> Result<()> {
        let (pc, _sym) = self.pc(dbg)?;

        if let Some(&(m, b)) = self.bp_to_block.get(&bp) {
            if log::max_level() == log::Level::Trace {
                let module = &self.coverage.modules()[m];
                let block = &module.blocks()[b];
                let name = module.name().display();
                log::trace!("{:>16x}: {}+{:x}", pc, name, block.rva());
            }

            self.coverage.report_block_hit(m, b);
        } else {
            log::error!("no block for breakpoint");
        }

        Ok(())
    }

    fn try_on_poll(&mut self, dbg: &mut Debugger) -> Result<()> {
        if !self.timed_out && self.started.elapsed() > self.max_duration {
            self.timed_out = true;
            dbg.quit_debugging();
        }

        Ok(())
    }
}

impl DebugEventHandler for BlockCoverageHandler {
    fn on_create_process(&mut self, dbg: &mut Debugger, module: &Module) {
        if self.try_on_create_process(dbg, module).is_err() {
            self.stop(dbg);
        }
    }

    fn on_load_dll(&mut self, dbg: &mut Debugger, module: &Module) {
        if self.try_on_load_dll(dbg, module).is_err() {
            self.stop(dbg);
        }
    }

    fn on_breakpoint(&mut self, dbg: &mut Debugger, bp: BreakpointId) {
        if self.try_on_breakpoint(dbg, bp).is_err() {
            self.stop(dbg);
        }
    }

    fn on_poll(&mut self, dbg: &mut Debugger) {
        if self.try_on_poll(dbg).is_err() {
            self.stop(dbg);
        }
    }
}
