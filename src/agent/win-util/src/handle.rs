// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use windows::Win32::{
    Foundation::{
        CloseHandle, DuplicateHandle, DUPLICATE_SAME_ACCESS, FALSE, HANDLE, INVALID_HANDLE_VALUE,
    },
    System::Threading::GetCurrentProcess,
};

pub struct Handle(pub HANDLE);

impl Handle {
    fn try_drop(&mut self) -> anyhow::Result<()> {
        unsafe { CloseHandle(self.0) }.ok()?;
        self.0 = INVALID_HANDLE_VALUE;
        Ok(())
    }
}

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
        // ignore any error
        _ = self.try_drop();
    }
}

unsafe impl Send for Handle {}

unsafe impl Sync for Handle {}

#[cfg(test)]
mod test {
    use windows::Win32::System::Threading::{GetCurrentProcessId, OpenProcess, PROCESS_ALL_ACCESS};

    use super::*;

    #[test]
    fn handle_clone() {
        // get a real (not pseudo) handle to play with
        let mut handle1 = Handle(
            unsafe { OpenProcess(PROCESS_ALL_ACCESS, false, GetCurrentProcessId()) }.unwrap(),
        );

        let mut handle2 = handle1.clone();
        assert_ne!(handle1.0, handle2.0);
        handle1.try_drop().unwrap();
        assert_eq!(handle1.0, INVALID_HANDLE_VALUE);
        handle2.try_drop().unwrap();
        assert_eq!(handle2.0, INVALID_HANDLE_VALUE);
    }
}
