// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![allow(clippy::fn_to_numeric_cast_with_truncation)]

use std::mem::{size_of, MaybeUninit};

use anyhow::Result;
use winapi::{
    shared::minwindef::{DWORD, FALSE, LPVOID},
    um::{
        memoryapi::VirtualQueryEx,
        psapi::{K32GetPerformanceInfo, PERFORMANCE_INFORMATION},
        winnt::{
            HANDLE, MEMORY_BASIC_INFORMATION, PAGE_EXECUTE, PAGE_EXECUTE_READ,
            PAGE_EXECUTE_READWRITE, PAGE_EXECUTE_WRITECOPY,
        },
    },
};

use crate::last_os_error;

pub struct MemoryInfo {
    base_address: u64,
    region_size: u64,
    protection: DWORD,
}

impl MemoryInfo {
    pub fn new(base_address: u64, region_size: u64, protection: DWORD) -> Self {
        Self {
            base_address,
            region_size,
            protection,
        }
    }

    pub fn base_address(&self) -> u64 {
        self.base_address
    }

    pub fn region_size(&self) -> u64 {
        self.region_size
    }

    pub fn is_executable(&self) -> bool {
        0 != (self.protection
            & (PAGE_EXECUTE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY))
    }
}

pub fn get_memory_info(process_handle: HANDLE, address: u64) -> Result<MemoryInfo> {
    let mut mbi = MaybeUninit::zeroed();
    let size = unsafe {
        VirtualQueryEx(
            process_handle,
            address as LPVOID,
            mbi.as_mut_ptr(),
            size_of::<MEMORY_BASIC_INFORMATION>(),
        )
    };
    if size != size_of::<MEMORY_BASIC_INFORMATION>() {
        return Err(last_os_error());
    }

    let mbi = unsafe { mbi.assume_init() };
    Ok(MemoryInfo::new(
        mbi.BaseAddress as u64,
        mbi.RegionSize as u64,
        mbi.Protect,
    ))
}

pub struct SystemMemoryInfo {
    pub commit_total: usize,
    pub commit_limit: usize,
    pub commit_peak: usize,
    pub physical_total: usize,
    pub physical_available: usize,
    pub kernel_total: usize,
    pub kernel_paged: usize,
    pub kernel_nonpaged: usize,
    pub handle_count: u32,
    pub process_count: u32,
    pub thread_count: u32,
}

impl From<PERFORMANCE_INFORMATION> for SystemMemoryInfo {
    fn from(info: PERFORMANCE_INFORMATION) -> Self {
        let page_size = info.PageSize;

        Self {
            commit_total: info.CommitTotal * page_size,
            commit_limit: info.CommitLimit * page_size,
            commit_peak: info.CommitPeak * page_size,
            physical_total: info.PhysicalTotal * page_size,
            physical_available: info.PhysicalAvailable * page_size,
            kernel_total: info.KernelTotal * page_size,
            kernel_paged: info.KernelPaged * page_size,
            kernel_nonpaged: info.KernelNonpaged * page_size,
            handle_count: info.HandleCount,
            process_count: info.ProcessCount,
            thread_count: info.ThreadCount,
        }
    }
}

pub fn get_system_memory_info() -> Result<SystemMemoryInfo> {
    let mut info = MaybeUninit::zeroed();
    if unsafe {
        K32GetPerformanceInfo(info.as_mut_ptr(), size_of::<PERFORMANCE_INFORMATION> as u32)
    } == FALSE
    {
        return Err(last_os_error());
    }

    let info = unsafe { info.assume_init() };

    Ok(info.into())
}
