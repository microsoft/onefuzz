// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![allow(clippy::fn_to_numeric_cast_with_truncation)]

use std::{
    ffi::c_void,
    mem::{size_of, MaybeUninit},
};

use crate::last_os_error;
use anyhow::Result;
use windows::Win32::{
    Foundation::HANDLE,
    System::{
        Memory::{
            VirtualQueryEx, MEMORY_BASIC_INFORMATION, PAGE_EXECUTE, PAGE_EXECUTE_READ,
            PAGE_EXECUTE_READWRITE, PAGE_EXECUTE_WRITECOPY, PAGE_PROTECTION_FLAGS,
        },
        ProcessStatus::{GetPerformanceInfo, PERFORMANCE_INFORMATION},
    },
};

pub struct MemoryInfo {
    base_address: u64,
    region_size: u64,
    protection: PAGE_PROTECTION_FLAGS,
}

impl MemoryInfo {
    pub fn new(base_address: u64, region_size: u64, protection: PAGE_PROTECTION_FLAGS) -> Self {
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
        (self.protection
            & (PAGE_EXECUTE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY))
            != PAGE_PROTECTION_FLAGS::default()
    }
}

pub fn get_memory_info(process_handle: HANDLE, address: u64) -> Result<MemoryInfo> {
    let mut mbi = MaybeUninit::zeroed();
    let size = unsafe {
        VirtualQueryEx(
            process_handle,
            Some(address as *const c_void),
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
    const SIZE: u32 = size_of::<PERFORMANCE_INFORMATION>() as u32;
    unsafe { GetPerformanceInfo(info.as_mut_ptr(), SIZE).ok()? };

    let info = unsafe { info.assume_init() };
    Ok(info.into())
}
