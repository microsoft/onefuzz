// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::{BTreeMap, BTreeSet};
use std::ffi::OsStr;
use std::path::{Path, PathBuf};
use std::process::Command;

use anyhow::{format_err, Result};
use goblin::elf;
use iced_x86::{Decoder, DecoderOptions, Instruction};
use pete::{Ptracer, Restart, Signal, Stop, Tracee};
use procfs::process::{MMapPath, MemoryMap, Process};
use serde::{Deserialize, Serialize};

use crate::block::ModuleCov;

pub fn record(cmd: Command) -> Result<CommandBlockCov> {
    let mut recorder = Recorder::default();
    recorder.record(cmd)?;
    Ok(recorder.coverage)
}

#[derive(Default, Debug)]
pub struct Recorder {
    breakpoints: Breakpoints,
    coverage: CommandBlockCov,
    images: Option<Images>,
}

impl Recorder {
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
            self.on_module_load(tracee, image)?;
        }

        Ok(())
    }

    fn on_breakpoint(&mut self, tracee: &mut Tracee) -> Result<()> {
        let mut regs = tracee.registers()?;

        log::trace!("hit breakpoint: {:x} (~{})", regs.rip - 1, tracee.pid);

        // Adjust for synthetic `int3`.
        let pc = regs.rip - 1;

        if self.breakpoints.clear(tracee, pc)? {
            let images = self
                .images
                .as_ref()
                .ok_or_else(|| format_err!("internal error: recorder images not initialized"))?;
            let image = images
                .find_va_image(pc)
                .ok_or_else(|| format_err!("unable to find image for va = {:x}", pc))?;

            self.coverage.increment(&image, pc)?;

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
        log::info!("module load: {}", image.path().display());

        let blocks = find_module_blocks(image.path())?;

        log::debug!("found {} blocks", blocks.len());

        if blocks.is_empty() {
            // This almost certainly means the binary was stripped of symbols.
            log::warn!("no blocks for module, not setting breakpoints");
            return Ok(());
        }

        let new = self.coverage.add_module(image, &blocks)?;

        if new {
            for b in blocks {
                let va = image.offset_to_va(b.offset);
                log::trace!("set breakpoint: {:x} (~{})", va, tracee.pid);
                self.breakpoints.set(tracee, va)?;
            }
        }

        Ok(())
    }
}

/// Block coverage for a command invocation.
///
/// Organized by module.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct CommandBlockCov {
    pub modules: BTreeMap<PathBuf, ModuleCov>,
}

impl CommandBlockCov {
    pub fn add_module(&mut self, image: &ModuleImage, blocks: &[Block]) -> Result<bool> {
        if self.modules.contains_key(image.path()) {
            return Ok(false);
        }

        let blocks = blocks.iter().map(|b| b.offset);
        let cov = ModuleCov::new(image.path(), blocks);

        self.modules.insert(image.path().to_owned(), cov);

        Ok(true)
    }

    pub fn increment(&mut self, image: &ModuleImage, va: u64) -> Result<()> {
        if let Some(cov) = self.modules.get_mut(image.path()) {
            let offset = image.va_to_offset(va);
            if let Some(block) = cov.blocks.get_mut(&offset) {
                block.count += 1;
            }
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
}

impl ModuleImage {
    pub fn new(map: MemoryMap) -> Result<Self> {
        if let MMapPath::Path(..) = &map.pathname {
            if map.perms.contains('x') {
                Ok(ModuleImage { map })
            } else {
                anyhow::bail!("memory mapping is not executable");
            }
        } else {
            anyhow::bail!("memory mapping is not file-backed");
        }
    }

    pub fn name(&self) -> &OsStr {
        // File name existence guaranteed by how we acquired the `Path`.
        self.path().file_name().unwrap()
    }

    pub fn path(&self) -> &Path {
        if let MMapPath::Path(path) = &self.map.pathname {
            return &path;
        }

        // Enforced by ctor.
        unreachable!()
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

#[derive(Clone, Copy, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct Block {
    pub offset: u64,
}

pub fn find_module_blocks(module: &Path) -> Result<Vec<Block>> {
    use elf::program_header::PT_LOAD;

    let data = std::fs::read(module)?;
    let object = elf::Elf::parse(&data)?;

    // Calculate the module base address as the lowest preferred VA of any loadable segment.
    //
    // https://refspecs.linuxbase.org/elf/gabi4+/ch5.pheader.html#base_address
    let base_va = object
        .program_headers
        .iter()
        .filter(|h| h.p_type == PT_LOAD)
        .map(|h| h.p_vaddr)
        .min()
        .ok_or_else(|| format_err!("no loadable segments for ELF object ({})", module.display()))?;

    let mut leaders = BTreeSet::new();

    for sym in object.syms.iter() {
        if sym.st_size == 0 {
            continue;
        }

        if sym.is_function() {
            let section = object
                .section_headers
                .get(sym.st_shndx)
                .cloned()
                .ok_or_else(|| format_err!("invalid section table index for symbol"))?;
            let sym_leaders = find_symbol_block_leaders(&data, section, sym)?;
            leaders.extend(sym_leaders);
        }
    }

    // Translate leader VAs to blocks, with block entry points represented as offsets
    // from the module image base.
    let mut blocks = vec![];

    for va in leaders {
        // Calculate image offsets (RVAs).
        let offset = va - base_va;
        blocks.push(Block { offset });
    }

    Ok(blocks)
}

/// From the raw file data and containing section header, find the virtual addrs for the
/// block leaders of the function `sym`.
///
/// Assumes `sym` is a function symbol (has type `STT_FUNC`) with nonzero size.
pub fn find_symbol_block_leaders(
    data: &[u8],
    section: elf::SectionHeader,
    sym: elf::Sym,
) -> Result<BTreeSet<u64>> {
    // For executables and shared objects, `st_value` contains the VA of the symbol.
    //
    // https://refspecs.linuxbase.org/elf/gabi4+/ch4.symtab.html#symbol_value
    let sym_va = sym.st_value;

    // If mapped into a segment, `sh_addr` contains the VA of the section image, consistent with
    // the `p_vaddr` of the segment.
    //
    // https://refspecs.linuxbase.org/elf/gabi4+/ch4.sheader.html#section_header
    let section_va = section.sh_addr;

    // The offset of the symbol, relative to the section (both in-file and when mapped).
    let sym_section_offset = sym_va - section_va;

    // We have the file offset for the section, and the offset of the symbol
    // relative to the section. From these, calculate the file offset for the
    // symbol, which we can use to index into `data`.
    let sym_file_offset = section.sh_offset + sym_section_offset;

    // Extract symbol's instruction data from file.
    let sym_data = {
        let lo = sym_file_offset as usize;
        // Checked by caller: `st_size` is nonzero.
        let hi = lo + (sym.st_size as usize);
        &data[lo..hi]
    };

    let mut decoder = Decoder::new(64, sym_data, DecoderOptions::NONE);
    decoder.set_ip(sym_va);

    // Contains leaders with VAs, assuming section load address.
    let mut leaders = BTreeSet::new();

    // Function entry is a leader.
    leaders.insert(sym_va);

    let mut inst = Instruction::default();
    while decoder.can_decode() {
        decoder.decode_out(&mut inst);

        if let Some((target, conditional)) = branch_target(&inst) {
            // The branch target is a leader.
            leaders.insert(target);

            // Only mark the next instruction as a leader if the branch is conditional.
            // This will give an invalid basic block decomposition if the leaders we emit
            // are used as delimiters. In particular, blocks that end with a `jmp` will be
            // too large, and have an unconditional branch mid-block.
            //
            // However, we only care about the leaders as block entry points, so we can
            // set software breakpoints. These maybe-unreachable leaders are a liability
            // wrt mutating the running process' code, so we discard them for now.
            if conditional {
                // The next instruction is a leader, if it exists.
                if decoder.can_decode() {
                    // We decoded the current instruction, so the decoder offset is
                    // set to the next instruction.
                    let next = decoder.ip() as u64;
                    leaders.insert(next);
                }
            }
        }
    }

    Ok(leaders)
}

// Returns the virtual address of a branch target, if present, with a flag that
// is true when the branch is conditional.
fn branch_target(inst: &iced_x86::Instruction) -> Option<(u64, bool)> {
    use iced_x86::FlowControl;

    match inst.flow_control() {
        FlowControl::ConditionalBranch => Some((inst.near_branch_target(), true)),
        FlowControl::UnconditionalBranch => Some((inst.near_branch_target(), false)),
        FlowControl::Call
        | FlowControl::Exception
        | FlowControl::IndirectBranch
        | FlowControl::IndirectCall
        | FlowControl::Interrupt
        | FlowControl::Next
        | FlowControl::Return
        | FlowControl::XbeginXabortXend => None,
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
