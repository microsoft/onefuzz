use winapi::{
    shared::minwindef::{FALSE, LPHANDLE},
    um::{
        handleapi::{CloseHandle, DuplicateHandle, INVALID_HANDLE_VALUE},
        processthreadsapi::GetCurrentProcess,
        winnt::{DUPLICATE_SAME_ACCESS, HANDLE},
    },
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
                &mut duplicate as LPHANDLE,
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
