// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

extern crate libc;

pub fn check(data: &[u8]) -> bool {
    if data.len() < 4 {
        return false;
    }

    if data[0] != 0x41 {
        return false;
    }

    if data[1] != 0x42 {
        return false;
    }

    if data[2] != 0x43 {
        return false;
    }

    match data[3] {
        // OOB access
        4 => data[100000] == 0xFF,
        // null ptr
        5 => unsafe {
            let ptr: *mut u8 = 0 as *mut u8;
            *ptr = 10;
            true
        },
        _ => false,
    }
}
