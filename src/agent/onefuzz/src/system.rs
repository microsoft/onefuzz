// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::sync::RwLock;

use anyhow::Result;
use sysinfo::{Pid, ProcessExt, ProcessorExt, SystemExt};

pub fn refresh() -> Result<()> {
    let mut s = SYSTEM.write().map_err(|e| format_err!("{}", e))?;
    s.refresh();
    Ok(())
}

pub fn system_info() -> Result<SystemInfo> {
    let s = SYSTEM.read().map_err(|e| format_err!("{}", e))?;
    Ok(s.system_info())
}

pub fn processes() -> Result<Vec<ProcInfo>> {
    let mut s = SYSTEM.write().map_err(|e| format_err!("{}", e))?;
    Ok(s.processes())
}

pub fn proc_info(pid: u32) -> Result<Option<ProcInfo>> {
    let s = SYSTEM.read().map_err(|e| format_err!("{}", e))?;
    Ok(s.proc_info(pid))
}

pub fn refresh_process(pid: u32) -> Result<bool> {
    let mut s = SYSTEM.write().map_err(|e| format_err!("{}", e))?;
    Ok(s.refresh_process(pid))
}

lazy_static! {
    static ref SYSTEM: RwLock<System> = {
        let mut s = System::new();
        s.refresh();
        RwLock::new(s)
    };
}

struct System {
    system: sysinfo::System,
}

// Mark our private `System` wrapper as `Send` and `Sync`, we can make a global.
//
// We may mark the type as `Sync` because we will wrap a `RwLock` around the only instance.
// We will synchronize access to the instance within the functions exported from this module.
//
// We may mark the type as `Send` because we only have one global instance, which is private,
// and we will not move it across threads. It is not actually `Send`, and must not be exported.
unsafe impl Send for System {}
unsafe impl Sync for System {}

impl System {
    pub fn new() -> Self {
        let mut system = sysinfo::System::new_all();
        system.refresh_all();

        Self { system }
    }

    pub fn refresh(&mut self) {
        self.system.refresh_all();
    }

    pub fn refresh_process(&mut self, pid: u32) -> bool {
        self.system.refresh_process(pid as Pid)
    }

    pub fn system_info(&self) -> SystemInfo {
        let system = &self.system;

        let total_memory_kib = system.get_total_memory();
        let used_memory_kib = system.get_used_memory();
        let free_memory_kib = system.get_free_memory();
        let total_swap_kib = system.get_total_swap();
        let used_swap_kib = system.get_used_swap();
        let uptime = system.get_uptime();

        let load_avg = system.get_load_average();
        let load_avg_1min = load_avg.one;
        let load_avg_5min = load_avg.five;
        let load_avg_15min = load_avg.fifteen;

        let global_cpu = system.get_global_processor_info();
        let cpu_usage = global_cpu.get_cpu_usage();

        SystemInfo {
            total_memory_kib,
            used_memory_kib,
            free_memory_kib,
            total_swap_kib,
            used_swap_kib,
            uptime,
            load_avg_1min,
            load_avg_5min,
            load_avg_15min,
            cpu_usage,
        }
    }

    pub fn processes(&mut self) -> Vec<ProcInfo> {
        self.system.refresh_processes();

        let mut results = vec![];
        for (pid, pi) in self.system.get_processes() {
            let pid = *pid as u32;
            let name = pi.name().into();
            let status = format!("{}", pi.status());
            let cpu_usage = pi.cpu_usage();
            let memory_kb = pi.memory();
            let virtual_memory_kb = pi.virtual_memory();

            results.push(ProcInfo {
                name,
                pid,
                status,
                cpu_usage,
                memory_kb,
                virtual_memory_kb,
            });
        }
        results
    }

    pub fn proc_info(&self, pid: u32) -> Option<ProcInfo> {
        let system = &self.system;
        let pi = system.get_process(pid as Pid)?;

        let name = pi.name().into();
        let status = format!("{}", pi.status());
        let cpu_usage = pi.cpu_usage();
        let memory_kb = pi.memory();
        let virtual_memory_kb = pi.virtual_memory();

        Some(ProcInfo {
            name,
            pid,
            status,
            cpu_usage,
            memory_kb,
            virtual_memory_kb,
        })
    }
}

#[derive(Clone, Debug)]
pub struct SystemInfo {
    pub total_memory_kib: u64,
    pub used_memory_kib: u64,
    pub free_memory_kib: u64,
    pub total_swap_kib: u64,
    pub used_swap_kib: u64,
    pub uptime: u64,
    pub load_avg_1min: f64,
    pub load_avg_5min: f64,
    pub load_avg_15min: f64,
    pub cpu_usage: f32,
}

#[derive(Clone, Debug)]
pub struct ProcInfo {
    pub name: String,
    pub pid: u32,
    pub status: String,
    pub cpu_usage: f32,
    pub memory_kb: u64,
    pub virtual_memory_kb: u64,
}
