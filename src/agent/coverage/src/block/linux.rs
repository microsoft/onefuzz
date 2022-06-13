// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::BTreeMap;
use std::convert::TryInto;
use std::ffi::OsStr;
use std::process::Command;
use std::sync::mpsc;
use std::thread;
use std::time::{Duration, Instant};

use anyhow::{format_err, Context, Result};
use pete::{Ptracer, Restart, Signal, Stop, Tracee};
use procfs::process::{MMapPath, MemoryMap, Process};

use crate::block::CommandBlockCov;
use crate::cache::ModuleCache;
use crate::code::{CmdFilter, ModulePath};
use crate::demangle::Demangler;
use crate::region::Region;

#[derive(Debug)]
pub struct Recorder<'c> {
    breakpoints: Breakpoints,
    cache: &'c mut ModuleCache,
    coverage: CommandBlockCov,
    demangler: Demangler,
    filter: CmdFilter,
    images: Option<Images>,
    tracer: Ptracer,
}

impl<'c> Recorder<'c> {
    pub fn record(
        cmd: Command,
        timeout: Duration,
        cache: &'c mut ModuleCache,
        filter: CmdFilter,
    ) -> Result<CommandBlockCov> {
        let mut tracer = Ptracer::new();
        let mut child = tracer.spawn(cmd)?;

        let _timer = Timer::new(timeout, move || child.kill());

        let recorder = Recorder {
            breakpoints: Breakpoints::default(),
            cache,
            coverage: CommandBlockCov::default(),
            demangler: Demangler::default(),
            filter,
            images: None,
            tracer,
        };

        let coverage = recorder.wait()?;

        Ok(coverage)
    }

    fn wait(mut self) -> Result<CommandBlockCov> {
        use pete::ptracer::Options;

        // Continue the tracee process until the return from its initial `execve()`.
        let mut tracee = continue_to_init_execve(&mut self.tracer)?;

        // Do not follow forks.
        //
        // After this, we assume that any new tracee is a thread in the same
        // group as the root tracee.
        let mut options = Options::all();
        options.remove(Options::PTRACE_O_TRACEFORK);
        options.remove(Options::PTRACE_O_TRACEVFORK);
        options.remove(Options::PTRACE_O_TRACEEXEC);
        tracee
            .set_options(options)
            .context("setting tracee options")?;

        self.images = Some(Images::new(tracee.pid.as_raw()));
        self.update_images(&mut tracee)
            .context("initial update of module images")?;

        self.tracer
            .restart(tracee, Restart::Syscall)
            .context("initial tracer restart")?;

        while let Some(mut tracee) = self.tracer.wait().context("main tracing loop")? {
            match tracee.stop {
                Stop::SyscallEnter => log::trace!("syscall-enter: {:?}", tracee.stop),
                Stop::SyscallExit => {
                    self.update_images(&mut tracee)
                        .context("updating module images after syscall-stop")?;
                }
                Stop::SignalDelivery {
                    signal: Signal::SIGTRAP,
                } => {
                    self.on_breakpoint(&mut tracee)
                        .context("calling breakpoint handler")?;
                }
                Stop::Clone { new: pid } => {
                    // Only seen when the `VM_CLONE` flag is set, as of Linux 4.15.
                    log::info!("new thread: {}", pid);
                }
                _ => {
                    log::debug!("stop: {:?}", tracee.stop);
                }
            }

            if let Err(err) = self.tracer.restart(tracee, Restart::Syscall) {
                log::error!("unable to restart tracee: {}", err);
            }
        }

        Ok(self.coverage)
    }

    fn update_images(&mut self, tracee: &mut Tracee) -> Result<()> {
        let images = self
            .images
            .as_mut()
            .ok_or_else(|| format_err!("internal error: recorder images not initialized"))?;
        let events = images.update()?;

        for (_base, image) in &events.loaded {
            if self.filter.includes_module(image.path()) {
                self.on_module_load(tracee, image)
                    .context("module load callback")?;
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

            let offset = image
                .va_to_offset(pc)
                .context("converting PC to module offset")?;
            self.coverage.increment(image.path(), offset);

            // Execute clobbered instruction on restart.
            regs.rip = pc;
            tracee
                .set_registers(regs)
                .context("resetting PC in breakpoint handler")?;
        } else {
            // Assume the tracee concurrently executed an `int3` that we restored
            // in another handler.
            //
            // We could improve on this by not removing breakpoints metadata when
            // clearing, but making their value a state.
            log::debug!("no breakpoint at {:x}, assuming race", pc);
            regs.rip = pc;
            tracee
                .set_registers(regs)
                .context("resetting PC after ignoring spurious breakpoint")?;
        }

        Ok(())
    }

    fn on_module_load(&mut self, tracee: &mut Tracee, image: &ModuleImage) -> Result<()> {
        log::info!("module load: {}", image.path());

        // Fetch disassembled module info via cache.
        let info = self
            .cache
            .fetch(image.path())?
            .ok_or_else(|| format_err!("unable to fetch info for module: {}", image.path()))?;

        // Collect blocks allowed by the symbol filter.
        let mut allowed_blocks = vec![];

        for symbol in info.module.symbols.iter() {
            // Try to demangle the symbol name for filtering. If no demangling
            // is found, fall back to the raw name.
            let symbol_name = self
                .demangler
                .demangle(&symbol.name)
                .unwrap_or_else(|| symbol.name.clone());

            // Check the maybe-demangled against the coverage filter.
            if self.filter.includes_symbol(&info.module.path, symbol_name) {
                // Convert range bounds to an `offset`-sized type.
                let range = {
                    let range = symbol.range();
                    let lo: u32 = range.start.try_into()?;
                    let hi: u32 = range.end.try_into()?;
                    lo..hi
                };

                for offset in info.blocks.range(range) {
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
#[derive(Clone, Debug, PartialEq, Eq)]
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
        let proc = Process::new(self.pid).context("getting procinfo")?;

        let mut new = BTreeMap::default();

        for map in proc.maps().context("getting maps for process")? {
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
                return Some(image);
            }
        }

        None
    }
}

/// A `MemoryMap` that is known to be file-backed and executable.
#[derive(Clone, Debug, PartialEq, Eq)]
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

    pub fn va_to_offset(&self, va: u64) -> Result<u32> {
        if let Some(offset) = va.checked_sub(self.base()) {
            Ok(offset.try_into().context("ELF offset overflowed `u32`")?)
        } else {
            anyhow::bail!("underflow converting VA to image offset")
        }
    }

    pub fn offset_to_va(&self, offset: u32) -> u64 {
        self.base() + (offset as u64)
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
                !old.iter()
                    .any(|(iva, i)| *nva == iva && n.path() == i.path())
            })
            .map(|(va, i)| (*va, i.clone()))
            .collect();

        // Old not in new.
        let unloaded: Vec<_> = old
            .iter()
            .filter(|(iva, i)| {
                !new.iter()
                    .any(|(nva, n)| nva == *iva && n.path() == i.path())
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
        tracee
            .write_memory(va, &[0xcc])
            .context("setting breakpoint, writing int3")?;

        Ok(())
    }

    pub fn clear(&mut self, tracee: &mut Tracee, va: u64) -> Result<bool> {
        let data = self.saved.remove(&va);

        let cleared = if let Some(data) = data {
            tracee
                .write_memory(va, &[data])
                .context("clearing breakpoint, restoring byte")?;
            true
        } else {
            false
        };

        Ok(cleared)
    }
}

fn continue_to_init_execve(tracer: &mut Ptracer) -> Result<Tracee> {
    while let Some(tracee) = tracer.wait()? {
        if let Stop::SyscallExit = &tracee.stop {
            return Ok(tracee);
        }

        tracer
            .restart(tracee, Restart::Continue)
            .context("restarting tracee pre-execve()")?;
    }

    anyhow::bail!("did not see initial execve() in tracee while recording coverage");
}

const MAX_POLL_PERIOD: Duration = Duration::from_millis(500);

pub struct Timer {
    sender: mpsc::Sender<()>,
    _handle: thread::JoinHandle<()>,
}

impl Timer {
    pub fn new<F, T>(timeout: Duration, on_timeout: F) -> Self
    where
        F: FnOnce() -> T + Send + 'static,
    {
        let (sender, receiver) = std::sync::mpsc::channel();

        let _handle = thread::spawn(move || {
            let poll_period = Duration::min(timeout, MAX_POLL_PERIOD);
            let start = Instant::now();

            while start.elapsed() < timeout {
                thread::sleep(poll_period);

                // Check if the timer has been cancelled.
                if let Err(mpsc::TryRecvError::Empty) = receiver.try_recv() {
                    continue;
                } else {
                    // We were cancelled or dropped, so return early and don't call back.
                    return;
                }
            }

            // Timed out, so call back.
            on_timeout();
        });

        Self { sender, _handle }
    }

    pub fn cancel(self) {
        // Drop `self`.
    }
}

impl Drop for Timer {
    fn drop(&mut self) {
        // Ignore errors, because they just mean the receiver has been dropped.
        let _ = self.sender.send(());
    }
}
