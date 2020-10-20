// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::BTreeMap;
use std::path::Path;
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

    pub fn pc(&self, dbg: &mut Debugger) -> (u64, Option<SymInfo>) {
        let rip = iced_x86::Register::RIP;
        let pc = dbg.read_register_u64(rip).unwrap();
        let sym = dbg.get_symbol(pc).ok();

        (pc, sym)
    }

    fn add_module(&mut self, dbg: &mut Debugger, path: &Path) {
        let bitset = crate::pe::process_image(path, false);
        if let Err(err) = bitset {
            log::warn!(
                "cannot record coverage for module = {}, err = {}",
                path.display(),
                err,
            );
            return;
        }
        let bitset = bitset.unwrap();

        let path = path.to_owned();
        let name = path.file_stem().unwrap().to_owned();
        let module = ModuleCoverageBlocks::new(path, name, bitset);

        let m = self.coverage.add_module(module);
        let module = &self.coverage.modules()[m];
        for (b, block) in module.blocks().iter().enumerate() {
            let bp = dbg.register_breakpoint(
                module.name(),
                block.rva() as u64,
                BreakpointType::OneTime,
            );
            self.bp_to_block.insert(bp, (m, b));
        }

        log::debug!(
            "inserted {} breakpoints for module {}",
            module.blocks().len(),
            module.path().display(),
        );
    }
}

impl DebugEventHandler for BlockCoverageHandler {
    fn on_create_process(&mut self, dbg: &mut Debugger, module: &Module) {
        dbg.target().sym_initialize().unwrap();

        log::info!("exe loaded: {}, {} bytes",
                 module.path().display(),
                 module.image_size(),
        );

        self.add_module(dbg, module.path());
    }

    fn on_load_dll(&mut self, dbg: &mut Debugger, module: &Module) {
        log::info!("dll loaded: {}, {} bytes",
                 module.path().display(),
                 module.image_size(),
        );

        self.add_module(dbg, module.path());
    }

    fn on_breakpoint(&mut self, dbg: &mut Debugger, bp: BreakpointId) {
        let (pc, _sym) = self.pc(dbg);

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
    }

    fn on_poll(&mut self, dbg: &mut Debugger) {
        if !self.timed_out && self.started.elapsed() > self.max_duration {
            self.timed_out = true;
            dbg.quit_debugging();
        }
    }
}
