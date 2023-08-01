// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::BTreeMap;

use anyhow::{bail, Result};
use debuggable_module::linux::LinuxModule;
use debuggable_module::load_module::LoadModule;
use debuggable_module::loader::Loader;
use debuggable_module::path::FilePath;
use debuggable_module::Address;
use pete::Tracee;

pub mod debugger;
use debugger::{DebugEventHandler, DebuggerContext, ModuleImage};

use crate::allowlist::AllowList;
use crate::binary::{BinaryCoverage, DebugInfoCache};

pub struct LinuxRecorder<'cache, 'data> {
    module_allowlist: AllowList,
    cache: &'cache DebugInfoCache,
    pub coverage: BinaryCoverage,
    loader: &'data Loader,
    modules: BTreeMap<FilePath, LinuxModule<'data>>,
}

impl<'cache, 'data> LinuxRecorder<'cache, 'data> {
    pub fn new(
        loader: &'data Loader,
        module_allowlist: AllowList,
        cache: &'cache DebugInfoCache,
    ) -> Self {
        let coverage = BinaryCoverage::default();
        let modules = BTreeMap::new();

        Self {
            allowlist,
            cache,
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

        #[cfg(target_arch = "x86_64")]
        let instruction_pointer = Address(regs.rip);

        #[cfg(target_arch = "aarch64")]
        let instruction_pointer = Address(regs.pc);

        if let Some(image) = context.find_image_for_addr(instruction_pointer) {
            if let Some(coverage) = self.coverage.modules.get_mut(image.path()) {
                let offset = instruction_pointer.offset_from(image.base())?;
                coverage.increment(offset);
            } else {
                bail!("coverage not initialized for module {}", image.path());
            }
        } else {
            bail!("no image for addr: {instruction_pointer:x}");
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

        if !self.module_allowlist.is_allowed(path) {
            debug!("not inserting denylisted module: {path}");
            return Ok(());
        }

        let module = if let Ok(module) = LinuxModule::load(self.loader, path.clone()) {
            module
        } else {
            debug!("skipping undebuggable module: {path}");
            return Ok(());
        };

        let coverage = self.cache.get_or_insert(&module)?.coverage;

        for offset in coverage.as_ref().keys().copied() {
            let addr = image.base().offset_by(offset)?;
            context.breakpoints.set(tracee, addr)?;
        }

        self.coverage.modules.insert(path.clone(), coverage);

        self.modules.insert(path.clone(), module);

        Ok(())
    }
}

impl<'cache, 'data> DebugEventHandler for LinuxRecorder<'cache, 'data> {
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
