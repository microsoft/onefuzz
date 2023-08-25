// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::BTreeMap;
use std::io::Read;
use std::process::{Child, Command};

use anyhow::{bail, format_err, Result};
use debuggable_module::path::FilePath;
use debuggable_module::Address;
use pete::{Ptracer, Restart, Signal, Stop, Tracee};
use procfs::process::{MMPermissions, MMapPath, MemoryMap, Process};

use crate::record::Output;

pub trait DebugEventHandler {
    fn on_breakpoint(&mut self, dbg: &mut DebuggerContext, tracee: &mut Tracee) -> Result<()>;

    fn on_module_load(
        &mut self,
        db: &mut DebuggerContext,
        tracee: &mut Tracee,
        image: &ModuleImage,
    ) -> Result<()>;
}

pub struct Debugger<'eh> {
    context: DebuggerContext,
    event_handler: &'eh mut dyn DebugEventHandler,
}

impl<'eh> Debugger<'eh> {
    pub fn new(event_handler: &'eh mut dyn DebugEventHandler) -> Self {
        let context = DebuggerContext::new();

        Self {
            context,
            event_handler,
        }
    }

    pub fn spawn(&mut self, cmd: Command) -> Result<Child> {
        Ok(self.context.tracer.spawn(cmd)?)
    }

    pub fn wait(self, mut child: Child) -> Result<Output> {
        if let Err(err) = self.wait_on_stops() {
            // Ignore error if child already exited.
            let _ = child.kill();

            return Err(err);
        }

        // Currently unavailable on Linux.
        let status = None;

        let stdout = if let Some(pipe) = &mut child.stdout {
            let mut stdout = Vec::new();
            pipe.read_to_end(&mut stdout)?;
            String::from_utf8_lossy(&stdout).into_owned()
        } else {
            "".into()
        };

        let stderr = if let Some(pipe) = &mut child.stderr {
            let mut stderr = Vec::new();
            pipe.read_to_end(&mut stderr)?;
            String::from_utf8_lossy(&stderr).into_owned()
        } else {
            "".into()
        };

        // Clean up, ignoring output that we've already gathered.
        //
        // These calls should also be unnecessary no-ops, but we really want to avoid any dangling
        // or zombie child processes.
        let _ = child.kill();
        let _ = child.wait();

        let output = Output {
            status,
            stderr,
            stdout,
        };

        Ok(output)
    }

    fn wait_on_stops(mut self) -> Result<()> {
        use pete::ptracer::Options;

        // Continue the tracee process until the return from its initial `execve()`.
        let mut tracee = continue_to_init_execve(&mut self.context.tracer)?;

        // Do not follow forks.
        //
        // After this, we assume that any new tracee is a thread in the same
        // group as the root tracee.
        let mut options = Options::all();
        options.remove(Options::PTRACE_O_TRACEFORK);
        options.remove(Options::PTRACE_O_TRACEVFORK);
        options.remove(Options::PTRACE_O_TRACEEXEC);
        tracee.set_options(options)?;

        // Initialize index of mapped modules now that we have a PID to query.
        self.context.images = Some(Images::new(tracee.pid.as_raw()));
        self.update_images(&mut tracee)?;

        // Restart tracee and enter the main debugger loop.
        self.context.tracer.restart(tracee, Restart::Syscall)?;

        while let Some(mut tracee) = self.context.tracer.wait()? {
            match tracee.stop {
                Stop::SyscallEnter => trace!("syscall-enter: {:?}", tracee.stop),
                Stop::SyscallExit => {
                    self.update_images(&mut tracee)?;
                }
                Stop::SignalDelivery {
                    signal: Signal::SIGTRAP,
                } => {
                    self.restore_and_call_if_breakpoint(&mut tracee)?;
                }
                Stop::Clone { new: pid } => {
                    // Only seen when the `VM_CLONE` flag is set, as of Linux 4.15.
                    info!("new thread: {}", pid);
                }
                _ => {
                    debug!("stop: {:?}", tracee.stop);
                }
            }

            if let Err(err) = self.context.tracer.restart(tracee, Restart::Syscall) {
                error!("unable to restart tracee: {}", err);
            }
        }

        Ok(())
    }

    fn restore_and_call_if_breakpoint(&mut self, tracee: &mut Tracee) -> Result<()> {
        let mut regs = tracee.registers()?;

        #[cfg(target_arch = "x86_64")]
        let instruction_pointer = &mut regs.rip;

        #[cfg(target_arch = "aarch64")]
        let instruction_pointer = &mut regs.pc;

        // Compute what the last PC would have been _if_ we stopped due to a soft breakpoint.
        //
        // If we don't have a registered breakpoint, then we will not use this value.
        let pc = Address(instruction_pointer.saturating_sub(1));

        if self.context.breakpoints.clear(tracee, pc)? {
            // We restored the original, `int3`-clobbered instruction in `clear()`. Now
            // set the tracee's registers to execute it on restart. Do this _before_ the
            // callback to simulate a hardware breakpoint.
            *instruction_pointer = pc.0;
            tracee.set_registers(regs)?;

            self.event_handler
                .on_breakpoint(&mut self.context, tracee)?;
        } else {
            warn!("no registered breakpoint for SIGTRAP delivery at {pc:x}");

            // We didn't fix up a registered soft breakpoint, so we have no reason to
            // re-execute the instruction at the last PC. Leave the tracee registers alone.
        }

        Ok(())
    }

    fn update_images(&mut self, tracee: &mut Tracee) -> Result<()> {
        let images = self
            .context
            .images
            .as_mut()
            .ok_or_else(|| format_err!("internal error: recorder images not initialized"))?;
        let events = images.update()?;

        for (_base, image) in &events.loaded {
            self.event_handler
                .on_module_load(&mut self.context, tracee, image)?;
        }

        Ok(())
    }
}

pub struct DebuggerContext {
    pub breakpoints: Breakpoints,
    pub images: Option<Images>,
    pub tracer: Ptracer,
}

impl DebuggerContext {
    #[allow(clippy::new_without_default)]
    pub fn new() -> Self {
        let breakpoints = Breakpoints::default();
        let images = None;
        let tracer = Ptracer::new();

        Self {
            breakpoints,
            images,
            tracer,
        }
    }

    pub fn find_image_for_addr(&self, addr: Address) -> Option<&ModuleImage> {
        self.images.as_ref()?.find_image_for_addr(addr)
    }
}

/// Executable memory-mapped files for a process.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Images {
    mapped: BTreeMap<Address, ModuleImage>,
    pid: i32,
}

impl Images {
    pub fn new(pid: i32) -> Self {
        let mapped = BTreeMap::default();

        Self { mapped, pid }
    }

    pub fn mapped(&self) -> impl Iterator<Item = (Address, &ModuleImage)> {
        self.mapped.iter().map(|(va, i)| (*va, i))
    }

    pub fn update(&mut self) -> Result<LoadEvents> {
        let proc = Process::new(self.pid)?;

        let mut new = BTreeMap::new();
        let mut group: Vec<MemoryMap> = vec![];

        for map in proc.maps()? {
            if let Some(last) = group.last() {
                if last.pathname != map.pathname {
                    // The current memory mapping is the start of a new group.
                    //
                    // Consume the current group, and track any new module image.
                    if let Ok(image) = ModuleImage::new(group) {
                        let base = image.base();
                        new.insert(base, image);
                    }

                    // Reset the current group.
                    group = vec![];
                }
            }

            group.push(map);
        }

        let events = LoadEvents::new(&self.mapped, &new);

        self.mapped = new;

        Ok(events)
    }

    pub fn find_image_for_addr(&self, addr: Address) -> Option<&ModuleImage> {
        let (_, image) = self.mapped().find(|(_, im)| im.contains(&addr))?;

        Some(image)
    }
}

/// A `MemoryMap` that is known to be file-backed and executable.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ModuleImage {
    base: Address,
    maps: Vec<MemoryMap>,
    path: FilePath,
}

impl ModuleImage {
    // Accepts an increasing sequence of memory mappings with a common file-backed
    // pathname.
    pub fn new(mut maps: Vec<MemoryMap>) -> Result<Self> {
        maps.sort_by_key(|m| m.address);

        if maps.is_empty() {
            bail!("no mapping for module image");
        }

        if !maps
            .iter()
            .any(|m| m.perms.contains(MMPermissions::EXECUTE))
        {
            bail!("no executable mapping for module image");
        }

        // Cannot panic due to initial length check.
        let first = &maps[0];

        let path = if let MMapPath::Path(path) = &first.pathname {
            FilePath::new(path.to_string_lossy())?
        } else {
            bail!("module image mappings must be file-backed");
        };

        for map in &maps {
            if map.pathname != first.pathname {
                bail!("module image mapping not file-backed");
            }
        }

        let base = Address(first.address.0);

        let image = ModuleImage { base, maps, path };

        Ok(image)
    }

    pub fn path(&self) -> &FilePath {
        &self.path
    }

    pub fn base(&self) -> Address {
        self.base
    }

    pub fn contains(&self, addr: &Address) -> bool {
        for map in &self.maps {
            let lo = Address(map.address.0);
            let hi = Address(map.address.1);
            if (lo..hi).contains(addr) {
                return true;
            }
        }

        false
    }
}

pub struct LoadEvents {
    pub loaded: Vec<(Address, ModuleImage)>,
    pub unloaded: Vec<(Address, ModuleImage)>,
}

impl LoadEvents {
    pub fn new(old: &BTreeMap<Address, ModuleImage>, new: &BTreeMap<Address, ModuleImage>) -> Self {
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
    saved: BTreeMap<Address, u8>,
}

impl Breakpoints {
    pub fn set(&mut self, tracee: &mut Tracee, addr: Address) -> Result<()> {
        // Return if the breakpoint exists. We don't want to conclude that the
        // saved instruction byte was `0xcc`.
        if self.saved.contains_key(&addr) {
            return Ok(());
        }

        let mut data = [0u8];
        tracee.read_memory_mut(addr.0, &mut data)?;
        self.saved.insert(addr, data[0]);
        tracee.write_memory(addr.0, &[0xcc])?;

        Ok(())
    }

    pub fn clear(&mut self, tracee: &mut Tracee, addr: Address) -> Result<bool> {
        let data = self.saved.remove(&addr);

        let cleared = if let Some(data) = data {
            tracee.write_memory(addr.0, &[data])?;
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

        tracer.restart(tracee, Restart::Continue)?;
    }

    bail!("did not see initial execve() in tracee while recording coverage");
}
