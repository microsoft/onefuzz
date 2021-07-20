// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![allow(clippy::trivially_copy_pass_by_ref)]
use anyhow::Result;
use pete::{
    Pid, Ptracer, Restart, Siginfo,
    Signal::{self, *},
    Stop, Tracee,
};
use proc_maps::MapRange;
use serde::Serialize;

use std::collections::BTreeMap;
use std::fmt;
use std::process::Command;

pub struct TriageCommand {
    tracer: Ptracer,
    tracee: Tracee,
    pid: Pid,
    _kill_on_drop: KillOnDrop,
}
impl TriageCommand {
    pub fn new(cmd: Command) -> Result<Self> {
        let mut tracer = Ptracer::new();

        let _child = tracer.spawn(cmd)?;

        // Continue the tracee process until the return from its initial `execve()`.
        let tracee = continue_to_init_execve(&mut tracer)?;

        // Save the PID of the target parent process.
        let pid = tracee.pid;

        let _kill_on_drop = KillOnDrop(pid);

        Ok(Self {
            tracer,
            tracee,
            pid,
            _kill_on_drop,
        })
    }

    pub fn pid(&self) -> Pid {
        self.pid
    }

    pub fn run(mut self) -> Result<TriageReport> {
        self.tracer.restart(self.tracee, Restart::Continue)?;

        let mut crashes = vec![];
        let mut exit_status = None;

        while let Some(tracee) = self.tracer.wait()? {
            match tracee.stop {
                Stop::SignalDelivery { signal } => {
                    if CRASH_SIGNALS.contains(&signal) {
                        // Can unwrap due to signal-delivery-stop.
                        let siginfo = tracee.siginfo()?.unwrap();
                        crashes.push(Crash::new(self.pid, signal, siginfo)?);
                    }
                }
                Stop::Exiting { exit_code } => {
                    exit_status = Some(ExitStatus::Exited(exit_code));
                }
                Stop::Signaling { signal, .. } => {
                    exit_status = Some(ExitStatus::Signaled(signal));
                }
                _ => {}
            }

            self.tracer.restart(tracee, Restart::Continue)?;
        }

        // We must observe either a normal or signaled exit for the parent.
        let exit_status = exit_status.unwrap();

        Ok(TriageReport {
            exit_status,
            crashes,
        })
    }
}

// Wrapper for a PID that signals it with SIGKILL when dropped.
//
// Lets us avoid an impl of `Drop` for `TriageCommand`, which constraints how
// we can move out of its fields in `run()`.
struct KillOnDrop(Pid);

impl Drop for KillOnDrop {
    fn drop(&mut self) {
        // The signaled PID may have already exited, if `run()` was invoked. That's fine,
        // since it has no side-effects, and we just want to handle the other case.
        let _ = nix::sys::signal::kill(self.0, SIGKILL);
    }
}

#[derive(Debug, Serialize)]
pub struct TriageReport {
    pub exit_status: ExitStatus,
    pub crashes: Vec<Crash>,
}

impl TriageReport {
    /// Did the target terminate due to a signal?
    pub fn signaled(&self) -> bool {
        matches!(self.exit_status, ExitStatus::Signaled(..))
    }

    /// Was the target signaled by a _crashing_ signal?
    pub fn crashed(&self) -> bool {
        self.signaled() && !self.crashes.is_empty()
    }
}

#[derive(Debug, Serialize)]
pub enum ExitStatus {
    #[serde(rename = "exited")]
    Exited(ExitCode),

    #[serde(rename = "signaled")]
    Signaled(#[serde(serialize_with = "se::signal")] Signal),
}

pub type ExitCode = i32;

#[derive(Debug, Serialize)]
pub struct Crash {
    /// Signal that caused the crash.
    #[serde(serialize_with = "se::signal")]
    pub signal: Signal,

    /// Address of crashing memory access associated with the signal, if any.
    pub crashing_access: Option<Address>,

    /// ID of the signaled thread.
    #[serde(serialize_with = "se::pid")]
    pub tid: Pid,

    /// All active threads at time of crash, including the crashing thread.
    pub threads: BTreeMap<i32, ThreadInfo>,
}

impl Crash {
    pub fn new(tid: Pid, signal: Signal, siginfo: Siginfo) -> Result<Self> {
        let mut stacktrace = rstack::TraceOptions::new();
        stacktrace
            .snapshot(true)
            .thread_names(true)
            .symbols(true)
            .ptrace_attach(false);

        let proc = stacktrace.trace(tid.as_raw() as u32)?;

        let crashing_access = segv_access_addr(siginfo).map(|a| a.into());

        let maps = proc_maps::get_process_maps(tid.as_raw())?;

        let mut threads = BTreeMap::new();

        for thread in proc.threads() {
            let mut callstack = vec![];

            for frame in thread.frames() {
                let addr = frame.ip();

                let module = find_module_rva(addr, &maps);

                let function = if let Some(symbol) = frame.symbol() {
                    let mangled = symbol.name();
                    let demangled = cpp_demangle::Symbol::new(&mangled)
                        .map(|s| s.to_string())
                        .unwrap_or_else(|_| mangled.into());

                    Some(Rva {
                        name: demangled,
                        offset: symbol.offset(),
                    })
                } else {
                    None
                };

                let addr = addr.into();

                callstack.push(Frame {
                    addr,
                    module,
                    function,
                });
            }

            let tid = Pid::from_raw(thread.id() as i32);
            let name = thread.name().map(|n| n.to_owned());

            let info = ThreadInfo {
                tid,
                name,
                callstack,
            };

            threads.insert(tid.as_raw(), info);
        }

        Ok(Crash {
            signal,
            crashing_access,
            tid,
            threads,
        })
    }
}

#[derive(Debug, Serialize)]
pub struct ThreadInfo {
    /// ID of the thread.
    #[serde(serialize_with = "se::pid")]
    pub tid: Pid,

    /// Name of the thread, if any.
    pub name: Option<String>,

    /// Stack frames, ordered from inner to outer.
    pub callstack: Vec<Frame>,
}

#[derive(Debug, Serialize)]
pub struct Frame {
    /// Virtual address of frame PC.
    pub addr: Address,

    /// Module-relative address of `addr`.
    ///
    /// Might not exist if the stack was corrupted.
    pub module: Option<Rva>,

    /// Function-relative address of `addr`, if resolved.
    pub function: Option<Rva>,
}

impl fmt::Display for Frame {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        if let (Some(function), Some(module)) = (&self.function, &self.module) {
            return write!(
                f,
                "0x{:x} in {}+0x{:x} ({}+0x{:x})",
                self.addr.0, function.name, function.offset, module.name, module.offset,
            );
        }

        if let Some(module) = &self.module {
            return write!(
                f,
                "0x{:x} in <unknown> ({}+0x{:x})",
                self.addr.0, module.name, module.offset,
            );
        }

        write!(f, "0x{:x} in <unknown> (corrupt stack)", self.addr.0)
    }
}

/// Virtual memory address expressed as a symbol-relative offset.
#[derive(Debug, Serialize)]
pub struct Rva {
    /// Name of symbol, which may be a function or a module.
    pub name: String,

    /// Offset into the _file_ that backs the symbol.
    pub offset: u64,
}

/// Virtual memory address, which may not be valid (e.g. in canonical form) with
/// respect to the architecture.
#[derive(Debug, Serialize)]
pub struct Address(#[serde(serialize_with = "se::lower_hex")] pub u64);

impl From<u64> for Address {
    fn from(addr: u64) -> Self {
        Address(addr)
    }
}

// Find the module-relative address of `addr`, if it exists.
fn find_module_rva(addr: u64, maps: &[MapRange]) -> Option<Rva> {
    let mapping = find_mapping(addr, maps)?;

    // Offset into the mapped image of some object file, but _not_
    // the file itself.
    let image_offset = addr - (mapping.start() as u64);

    // Offset into the _object file_ that backs the mapping.
    let offset = image_offset + (mapping.offset as u64);

    let name = mapping
        .filename()
        .as_ref()
        .map(|s| s.to_owned())
        .unwrap_or_else(|| "<unknown>".into());

    Some(Rva { name, offset })
}

// Find the memory mapping in `maps` that contains `addr`, if any.
fn find_mapping(addr: u64, maps: &[MapRange]) -> Option<&MapRange> {
    for map in maps {
        let lo = map.start() as u64;
        let hi = lo + (map.size() as u64);
        if (lo..hi).contains(&addr) {
            return Some(map);
        }
    }

    None
}

// Only 4 signals populate the `si_addr` field of `siginfo_t`. See `sigaction(2)`.
// On Linux, the most reliable one is SIGSEGV, which saves the address of the invalid
// memory access.
fn segv_access_addr(siginfo: Siginfo) -> Option<u64> {
    let is_segv = siginfo.si_signo == (SIGSEGV as i32);

    if is_segv {
        // Accessing a union, safe because we checked `si_signo`.
        let ptr = unsafe { siginfo.si_addr() };

        let addr = ptr as u64;

        Some(addr)
    } else {
        None
    }
}

const CRASH_SIGNALS: &[Signal] = &[SIGILL, SIGFPE, SIGSEGV, SIGBUS, SIGTRAP, SIGABRT];

// Custom serializer functions for remote types.
mod se {
    use pete::{Pid, Signal};
    use serde::Serializer;

    pub fn pid<S>(pid: &Pid, s: S) -> Result<S::Ok, S::Error>
    where
        S: Serializer,
    {
        s.serialize_i32(pid.as_raw())
    }

    pub fn signal<S>(sig: &Signal, s: S) -> Result<S::Ok, S::Error>
    where
        S: Serializer,
    {
        s.serialize_str(sig.as_ref())
    }

    pub fn lower_hex<S, T>(t: &T, s: S) -> Result<S::Ok, S::Error>
    where
        S: Serializer,
        T: std::fmt::LowerHex,
    {
        s.serialize_str(&format!("0x{:x}", t))
    }
}

fn continue_to_init_execve(tracer: &mut Ptracer) -> Result<Tracee> {
    while let Some(tracee) = tracer.wait()? {
        if let Stop::SyscallExit = &tracee.stop {
            return Ok(tracee);
        }

        tracer.restart(tracee, Restart::Continue)?;
    }

    anyhow::bail!("did not see initial execve() in tracee while triaging input");
}
