// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use windows::Win32::{
    Foundation::{
        CloseHandle, DuplicateHandle, DUPLICATE_SAME_ACCESS, FALSE, HANDLE, INVALID_HANDLE_VALUE,
    },
    System::Threading::GetCurrentProcess,
};

pub struct Handle(pub HANDLE);

impl Clone for Handle {
    fn clone(&self) -> Self {
        let mut duplicate = INVALID_HANDLE_VALUE;
        unsafe {
            let current_process = GetCurrentProcess();
            DuplicateHandle(
                current_process,
                self.0,
                current_process,
                &mut duplicate,
                0,
                FALSE,
                DUPLICATE_SAME_ACCESS,
            );
        }

        Self(duplicate)
    }
}

impl Drop for Handle {
    fn drop(&mut self) {
        unsafe { CloseHandle(self.0) };
    }
}

unsafe impl Send for Handle {}

unsafe impl Sync for Handle {}
