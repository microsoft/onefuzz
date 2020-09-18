// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

//! This module implements a simple smart pointer wrapping a COM pointer.
use std::{ops::Deref, ptr};

use winapi::{
    ctypes::c_void,
    shared::{guiddef::GUID, ntdef::HRESULT, winerror::SUCCEEDED},
    um::unknwnbase::IUnknown,
    Interface,
};

use crate::hr::HRESULTError;

/// A wrapper for a COM pointer that implements AddRef via the Clone trait
/// and Release via the Drop trait.
#[repr(C)]
pub struct ComPtr<T>(*mut T)
where
    T: Interface;

impl<T> ComPtr<T>
where
    T: Interface,
{
    fn from_raw(ptr: *mut T) -> ComPtr<T> {
        ComPtr(ptr)
    }

    /// # Safety
    ///
    /// For use with C/C++ APIs that use IID_PPV_ARGS - they take the GUID and
    /// an out parameter to return the COM object.
    pub unsafe fn new_with_uuid<F>(f: F) -> Result<Self, HRESULTError>
    where
        F: FnOnce(&GUID, *mut *mut c_void) -> HRESULT,
    {
        Self::new_with(|ptr| f(&T::uuidof(), ptr as *mut *mut c_void))
    }

    /// # Safety
    ///
    /// For use with APIs that return a new COM object through an out parameter.
    pub unsafe fn new_with<F>(f: F) -> Result<Self, HRESULTError>
    where
        F: FnOnce(*mut *mut T) -> HRESULT,
    {
        let mut ptr = ptr::null_mut();
        let hresult = f(&mut ptr);
        if SUCCEEDED(hresult) {
            Ok(ComPtr::from_raw(ptr))
        } else {
            if !ptr.is_null() {
                let ptr = ptr as *mut IUnknown;
                (*ptr).Release();
            }
            Err(HRESULTError(hresult))
        }
    }

    /// # Safety
    fn as_iunknown(&self) -> &IUnknown {
        unsafe { &*(self.0 as *mut IUnknown) }
    }

    /// # Safety
    ///
    /// Perform a "cast" via QueryInterface.
    pub fn query_to<U>(&self) -> Result<ComPtr<U>, HRESULTError>
    where
        U: Interface,
    {
        let mut ptr = ptr::null_mut();
        check_hr!(unsafe { self.as_iunknown().QueryInterface(&U::uuidof(), &mut ptr) });
        Ok(ComPtr::from_raw(ptr as *mut U))
    }
}

impl<T> Clone for ComPtr<T>
where
    T: Interface,
{
    fn clone(&self) -> Self {
        unsafe { self.as_iunknown().AddRef() };
        ComPtr::from_raw(self.0)
    }
}

impl<T> Deref for ComPtr<T>
where
    T: Interface,
{
    type Target = T;

    /// # Safety
    fn deref(&self) -> &T {
        unsafe { &*self.0 }
    }
}

impl<T> Drop for ComPtr<T>
where
    T: Interface,
{
    /// # Safety
    fn drop(&mut self) {
        unsafe { self.as_iunknown().Release() };
    }
}
