// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::BTreeMap;
use std::ffi::OsStr;
use std::process::Command;

use anyhow::{format_err, Result};
use pete::{Ptracer, Restart, Signal, Stop, Tracee};
use procfs::process::{MMapPath, MemoryMap, Process};

use crate::block::CommandBlockCov;
use crate::cache::ModuleCache;
use crate::code::{ModulePath, SymbolFilter};
use crate::filter::Filter;
use crate::region::Region;

pub fn record(cmd: Command) -> Result<CommandBlockCov> {
    let mut recorder = Recorder::default();
    recorder.record(cmd)?;
    Ok(recorder.coverage)
}

#[derive(Default, Debug)]
pub struct Recorder {
    breakpoints: Breakpoints,
    pub coverage: CommandBlockCov,
    images: Option<Images>,
    pub modules: ModuleCache,
    pub module_filter: Filter,
    pub symbol_filter: SymbolFilter,
}

impl Recorder {
    pub fn new(module_filter: Filter, symbol_filter: SymbolFilter) -> Self {
        Self {
            module_filter,
            symbol_filter,
            ..Self::default()
        }
    }

    pub fn record(&mut self, cmd: Command) -> Result<()> {
        use pete::ptracer::Options;

        let mut tracer = Ptracer::new();
        let _child = tracer.spawn(cmd)?;

        // Continue the tracee process until the return from its initial `execve()`.
        let mut tracee = continue_to_init_execve(&mut tracer)?;

        // Do not follow forks.
        //
        // After this, we assume that any new tracee is a thread in the same
        // group as the root tracee.
        let mut options = Options::all();
        options.remove(Options::PTRACE_O_TRACEFORK);
        options.remove(Options::PTRACE_O_TRACEVFORK);
        options.remove(Options::PTRACE_O_TRACEEXEC);
        tracee.set_options(options)?;

        self.images = Some(Images::new(tracee.pid.as_raw()));
        self.update_images(&mut tracee)?;

        tracer.restart(tracee, Restart::Syscall)?;

        while let Some(mut tracee) = tracer.wait()? {
            match tracee.stop {
                Stop::SyscallEnterStop(..) => log::trace!("syscall-enter: {:?}", tracee.stop),
                Stop::SyscallExitStop(..) => {
                    self.update_images(&mut tracee)?;
                }
                Stop::SignalDeliveryStop(_pid, Signal::SIGTRAP) => {
                    self.on_breakpoint(&mut tracee)?;
                }
                Stop::Clone(pid, tid) => {
                    // Only seen when the `VM_CLONE` flag is set, as of Linux 4.15.
                    log::info!("new thread: {} -> {}", pid, tid);
                }
                _ => {
                    log::debug!("stop: {:?}", tracee.stop);
                }
            }

            if let Err(err) = tracer.restart(tracee, Restart::Syscall) {
                log::error!("unable to restart tracee: {}", err);
            }
        }

        Ok(())
    }

    fn update_images(&mut self, tracee: &mut Tracee) -> Result<()> {
        let images = self
            .images
            .as_mut()
            .ok_or_else(|| format_err!("internal error: recorder images not initialized"))?;
        let events = images.update()?;

        for (_base, image) in &events.loaded {
            let pathname = image.path().path_lossy();

            if self.module_filter.is_allowed(pathname) {
                self.on_module_load(tracee, image)?;
            }
        }

        Ok(())
    }

    fn on_breakpoint(&mut self, tracee: &mut Tracee) -> Result<()> {
        let mut regs = tracee.registers()?;

        // Adjust for synthetic `int3`.
        let pc = regs.rip - 1;

        log::trace!("hit breakpoint: pc = {:x}, pid = {}", pc, tracee.pid);

        if self.breakpoints.clear(tracee, pc)? {
            let images = self
                .images
                .as_ref()
                .ok_or_else(|| format_err!("internal error: recorder images not initialized"))?;
            let image = images
                .find_va_image(pc)
                .ok_or_else(|| format_err!("unable to find image for va = {:x}", pc))?;

            let offset = image.va_to_offset(pc);
            self.coverage.increment(image.path(), offset)?;

            // Execute clobbered instruction on restart.
            regs.rip = pc;
            tracee.set_registers(regs)?;
        } else {
            // Assume the tracee concurrently executed an `int3` that we restored
            // in another handler.
            //
            // We could improve on this by not removing breakpoints metadata when
            // clearing, but making their value a state.
            log::debug!("no breakpoint at {:x}, assuming race", pc);
            regs.rip = pc;
            tracee.set_registers(regs)?;
        }

        Ok(())
    }

    fn on_module_load(&mut self, tracee: &mut Tracee, image: &ModuleImage) -> Result<()> {
        log::info!("module load: {}", image.path());

        // Fetch disassembled module info via cache.
        let info = self
            .modules
            .fetch(image.path())?
            .ok_or_else(|| format_err!("unable to fetch info for module: {}", image.path()))?;

        // Collect blocks allowed by the symbol filter.
        let mut allowed_blocks = vec![];

        for symbol in info.module.symbols.iter() {
            if self
                .symbol_filter
                .is_allowed(&info.module.path, &symbol.name)
            {
                for offset in info.blocks.range(symbol.range()) {
                    allowed_blocks.push(*offset);
                }
            }
        }

        // Initialize module coverage info.
        let new = self
            .coverage
            .insert(image.path(), allowed_blocks.iter().copied());

        // If module coverage is already initialized, we're done.
        if !new {
            return Ok(());
        }

        // Set breakpoints by module block entry points.
        for offset in &allowed_blocks {
            let va = image.offset_to_va(*offset);
            self.breakpoints.set(tracee, va)?;
            log::trace!("set breakpoint, va = {:x}, pid = {}", va, tracee.pid);
        }

        Ok(())
    }
}

/// Executable memory-mapped files for a process.
#[derive(Clone, Debug, PartialEq)]
pub struct Images {
    mapped: BTreeMap<u64, ModuleImage>,
    pid: i32,
}

impl Images {
    pub fn new(pid: i32) -> Self {
        let mapped = BTreeMap::default();

        Self { mapped, pid }
    }

    pub fn mapped(&self) -> impl Iterator<Item = (u64, &ModuleImage)> {
        self.mapped.iter().map(|(va, i)| (*va, i))
    }

    pub fn update(&mut self) -> Result<LoadEvents> {
        let proc = Process::new(self.pid)?;

        let mut new = BTreeMap::default();

        for map in proc.maps()? {
            if let Ok(image) = ModuleImage::new(map) {
                new.insert(image.base(), image);
            }
        }

        let events = LoadEvents::new(&self.mapped, &new);

        self.mapped = new;

        Ok(events)
    }

    pub fn find_va_image(&self, va: u64) -> Option<&ModuleImage> {
        for (base, image) in self.mapped() {
            if va < base {
                continue;
            }

            if image.region().contains(&va) {
                return Some(&image);
            }
        }

        None
    }
}

/// A `MemoryMap` that is known to be file-backed and executable.
#[derive(Clone, Debug, PartialEq)]
pub struct ModuleImage {
    map: MemoryMap,
    path: ModulePath,
}

impl ModuleImage {
    pub fn new(map: MemoryMap) -> Result<Self> {
        if let MMapPath::Path(path) = &map.pathname {
            if map.perms.contains('x') {
                // Copy the path into a wrapper type that encodes extra guarantees.
                let path = ModulePath::new(path.clone())?;

                Ok(ModuleImage { map, path })
            } else {
                anyhow::bail!("memory mapping is not executable");
            }
        } else {
            anyhow::bail!("memory mapping is not file-backed");
        }
    }

    pub fn name(&self) -> &OsStr {
        self.path.name()
    }

    pub fn path(&self) -> &ModulePath {
        &self.path
    }

    pub fn map(&self) -> &MemoryMap {
        &self.map
    }

    pub fn base(&self) -> u64 {
        self.map.address.0 - self.map.offset
    }

    pub fn size(&self) -> u64 {
        self.map.address.1 - self.map.address.0
    }

    pub fn region(&self) -> std::ops::Range<u64> {
        (self.map.address.0)..(self.map.address.1)
    }

    pub fn va_to_offset(&self, va: u64) -> u64 {
        va - self.base()
    }

    pub fn offset_to_va(&self, offset: u64) -> u64 {
        self.base() + offset
    }
}

pub struct LoadEvents {
    pub loaded: Vec<(u64, ModuleImage)>,
    pub unloaded: Vec<(u64, ModuleImage)>,
}

impl LoadEvents {
    pub fn new(old: &BTreeMap<u64, ModuleImage>, new: &BTreeMap<u64, ModuleImage>) -> Self {
        // New not in old.
        let loaded: Vec<_> = new
            .iter()
            .filter(|(nva, n)| {
                old.iter()
                    .find(|(iva, i)| nva == iva && n.path() == i.path())
                    .is_none()
            })
            .map(|(va, i)| (*va, i.clone()))
            .collect();

        // Old not in new.
        let unloaded: Vec<_> = old
            .iter()
            .filter(|(iva, i)| {
                new.iter()
                    .find(|(nva, n)| nva == iva && n.path() == i.path())
                    .is_none()
            })
            .map(|(va, i)| (*va, i.clone()))
            .collect();

        Self { loaded, unloaded }
    }
}

#[derive(Clone, Debug, Default)]
pub struct Breakpoints {
    saved: BTreeMap<u64, u8>,
}

impl Breakpoints {
    pub fn set(&mut self, tracee: &mut Tracee, va: u64) -> Result<()> {
        // Return if the breakpoint exists. We don't want to conclude that the
        // saved instruction byte was `0xcc`.
        if self.saved.contains_key(&va) {
            return Ok(());
        }

        let mut data = [0u8];
        tracee.read_memory_mut(va, &mut data)?;
        self.saved.insert(va, data[0]);
        tracee.write_memory(va, &[0xcc])?;

        Ok(())
    }

    pub fn clear(&mut self, tracee: &mut Tracee, va: u64) -> Result<bool> {
        let data = self.saved.remove(&va);

        let cleared = if let Some(data) = data {
            tracee.write_memory(va, &[data])?;
            true
        } else {
            false
        };

        Ok(cleared)
    }
}

fn continue_to_init_execve(tracer: &mut Ptracer) -> Result<Tracee> {
    while let Some(tracee) = tracer.wait()? {
        if let Stop::SyscallExitStop(..) = &tracee.stop {
            return Ok(tracee);
        }

        tracer.restart(tracee, Restart::Continue)?;
    }

    anyhow::bail!("did not see initial execve() in tracee while recording coverage");
}
