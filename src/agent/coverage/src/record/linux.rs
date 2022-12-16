// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::BTreeMap;
use std::process::Command;
use std::time::Duration;

use anyhow::{bail, Result};
use debuggable_module::linux::LinuxModule;
use debuggable_module::load_module::LoadModule;
use debuggable_module::loader::Loader;
use debuggable_module::path::FilePath;
use debuggable_module::Address;
use pete::Tracee;

pub mod debugger;
use debugger::{DebugEventHandler, Debugger, DebuggerContext, ModuleImage};

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
        let mut recorder = LinuxRecorder::new(&loader, allowlist);
        let dbg = Debugger::new(&mut recorder);
        dbg.run(cmd)?;

        Ok(recorder.coverage)
    })?
}

pub struct LinuxRecorder<'data> {
    allowlist: TargetAllowList,
    coverage: BinaryCoverage,
    loader: &'data Loader,
    modules: BTreeMap<FilePath, LinuxModule<'data>>,
}

impl<'data> LinuxRecorder<'data> {
    pub fn new(loader: &'data Loader, allowlist: TargetAllowList) -> Self {
        let coverage = BinaryCoverage::default();
        let modules = BTreeMap::new();

        Self {
            allowlist,
            coverage,
            loader,
            modules,
        }
    }

    fn do_on_breakpoint(
        &mut self,
        context: &mut DebuggerContext,
        tracee: &mut Tracee,
    ) -> Result<()> {
        let regs = tracee.registers()?;
        let addr = Address(regs.rip);

        if let Some(image) = context.find_image_for_addr(addr) {
            if let Some(coverage) = self.coverage.modules.get_mut(image.path()) {
                let offset = addr.offset_from(image.base())?;
                coverage.increment(offset)?;
            } else {
                bail!("coverage not initialized for module {}", image.path());
            }
        } else {
            bail!("no image for addr: {addr:x}");
        }

        Ok(())
    }

    fn do_on_module_load(
        &mut self,
        context: &mut DebuggerContext,
        tracee: &mut Tracee,
        image: &ModuleImage,
    ) -> Result<()> {
        info!("module load: {}", image.path());

        let path = image.path();

        if !self.allowlist.modules.is_allowed(path) {
            debug!("not inserting denylisted module: {path}");
            return Ok(());
        }

        let module = if let Ok(module) = LinuxModule::load(self.loader, path.clone()) {
            module
        } else {
            debug!("skipping undebuggable module: {path}");
            return Ok(());
        };

        let coverage = binary::find_coverage_sites(&module, &self.allowlist)?;

        for offset in coverage.as_ref().keys().copied() {
            let addr = image.base().offset_by(offset)?;
            context.breakpoints.set(tracee, addr)?;
        }

        self.coverage.modules.insert(path.clone(), coverage);

        self.modules.insert(path.clone(), module);

        Ok(())
    }
}

impl<'data> DebugEventHandler for LinuxRecorder<'data> {
    fn on_breakpoint(&mut self, context: &mut DebuggerContext, tracee: &mut Tracee) -> Result<()> {
        self.do_on_breakpoint(context, tracee)
    }

    fn on_module_load(
        &mut self,
        context: &mut DebuggerContext,
        tracee: &mut Tracee,
        image: &ModuleImage,
    ) -> Result<()> {
        self.do_on_module_load(context, tracee, image)
    }
}
