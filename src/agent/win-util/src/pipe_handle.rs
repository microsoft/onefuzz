// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

//! This module implements a wrapper around Win32 named pipes to support non-blocking reads.

#![allow(clippy::uninit_vec)]

use std::os::windows::io::{AsRawHandle, RawHandle};

use anyhow::{Context, Result};
use log::trace;
use winapi::{
    shared::{
        minwindef::{DWORD, FALSE, LPDWORD, LPVOID},
        ntdef::NULL,
        winerror::{ERROR_BROKEN_PIPE, ERROR_NO_DATA},
    },
    um::{
        errhandlingapi::GetLastError, fileapi::ReadFile, minwinbase::LPOVERLAPPED,
        namedpipeapi::SetNamedPipeHandleState, winbase::PIPE_NOWAIT,
    },
};

use crate::check_winapi;

// A wrapper over a Vec that uses RAII to set the length correctly after uses of the
// Vec internal buffer in unsafe Win32 apis that bypass safe Vec apis when writing data.
struct Guard<'a> {
    buf: &'a mut Vec<u8>,
    len: usize,
}

impl Drop for Guard<'_> {
    fn drop(&mut self) {
        unsafe {
            self.buf.set_len(self.len);
        }
    }
}

// A wrapper around a Win32 named pipe that does not block on read.
pub struct PipeReaderNonBlocking {
    reader: os_pipe::PipeReader,
}

impl PipeReaderNonBlocking {
    pub fn new(reader: os_pipe::PipeReader) -> Self {
        PipeReaderNonBlocking { reader }
    }

    // Read from the pipe, return how many bytes were read or the error.
    fn read_pipe(&self, buf: &mut Vec<u8>, to_end: bool) -> Result<usize> {
        let reservation_size = 128u32; // ~ 1 line of output

        let start_len = buf.len();

        // When the guard is dropped, the length of the buffer will be set to the correct length.
        let mut g = Guard {
            len: buf.len(),
            buf,
        };

        loop {
            // Reserve more buffer space if we need it. The check isn't strictly necessary,
            // but it does avoid reallocating the buffer too often if we had many small reads.
            if g.len == g.buf.len() {
                g.buf.reserve(reservation_size as usize);
                unsafe {
                    // We set the length so our slice below doesn't panic.
                    // We track the real length in the guard - setting it correctly via drop.
                    g.buf.set_len(g.buf.capacity());
                    // newly reserved memory is not initialized.
                }
            }

            let unused_space = &mut g.buf[g.len..];
            let unused_len = unused_space.len();
            let mut bytes_read = 0u32;
            let success = unsafe {
                ReadFile(
                    self.reader.as_raw_handle(),
                    unused_space.as_mut_ptr() as LPVOID,
                    unused_len as DWORD,
                    &mut bytes_read as LPDWORD,
                    NULL as LPOVERLAPPED,
                )
            };

            if success == FALSE {
                // We don't use std::io::Error::last_os_error because it treats
                // ERROR_NO_DATA and ERROR_BROKEN_PIPE identically.
                match unsafe { GetLastError() } {
                    ERROR_NO_DATA => {
                        // Non-blocking read and no data available, we can return now.
                        if !to_end {
                            break;
                        }
                    }
                    ERROR_BROKEN_PIPE => {
                        // A broken pipe is our EOF signal - not an error.
                        break;
                    }
                    _ => {
                        return Err(std::io::Error::last_os_error().into());
                    }
                }
            } else {
                g.len += bytes_read as usize;
            }
        }

        let total_bytes_read = g.len - start_len;
        if total_bytes_read > 0 {
            trace!(
                "Read {} bytes from pipe, `{}`",
                total_bytes_read,
                String::from_utf8_lossy(&g.buf[start_len..(start_len + total_bytes_read)])
            );
        }
        Ok(total_bytes_read)
    }

    /// Read the pipe into `buf`, reading until the pipe reports no more data.
    /// , returning number of bytes0
    pub fn read(&self, buf: &mut Vec<u8>) -> Result<usize> {
        self.read_pipe(buf, false)
    }

    /// Read the pipe into `buf`
    pub fn read_to_end(&self, buf: &mut Vec<u8>) -> Result<usize> {
        self.read_pipe(buf, true)
    }
}

fn set_nonblocking_mode(handle: RawHandle) -> Result<()> {
    let mut mode = PIPE_NOWAIT as DWORD;
    check_winapi(|| unsafe {
        SetNamedPipeHandleState(
            handle,
            &mut mode as LPDWORD,
            NULL as LPDWORD,
            NULL as LPDWORD,
        )
    })
    .context("Setting pipe to non-blocking mode")?;

    Ok(())
}

// Return a pair a reader and writer handle wrapping a Win32 named pipe.
// The writer can be converted to Stdio for a Command.
// The reader can be read from without blocking.
pub fn pipe() -> Result<(PipeReaderNonBlocking, os_pipe::PipeWriter)> {
    let (reader, writer) = os_pipe::pipe().context("Creating named pipes")?;
    let handle = reader.as_raw_handle();
    set_nonblocking_mode(handle)?;
    Ok((PipeReaderNonBlocking::new(reader), writer))
}

#[cfg(test)]
mod tests {
    use std::{
        io::Write,
        iter,
        sync::{mpsc, Arc},
        thread::{sleep, spawn},
        time::Duration,
    };

    use super::*;

    fn repeated_bytes(val: u8, count: usize) -> Vec<u8> {
        iter::repeat(val).take(count).collect::<Vec<u8>>()
    }

    #[test]
    fn read_empty_pipe() {
        let (reader, _writer) = pipe().unwrap();
        let mut buf = vec![];
        // The read is supposed to be non-blocking, so this should only hang (block)
        // if there is a bug.
        let bytes_read = reader.read(&mut buf).unwrap();
        assert_eq!(bytes_read, 0);
        assert_eq!(buf.len(), 0);
    }

    #[test]
    fn read_pipe() {
        let (reader, mut writer) = pipe().unwrap();
        let mut buf = vec![];

        assert!(writer.write_all(&[42]).is_ok());
        let bytes_read = reader.read(&mut buf).unwrap();
        assert_eq!(bytes_read, 1);
        assert_eq!(buf, vec![42]);

        assert!(writer.write_all(&[44, 48]).is_ok());
        let bytes_read = reader.read(&mut buf).unwrap();
        assert_eq!(bytes_read, 2);
        assert_eq!(buf, vec![42, 44, 48]);
    }

    #[test]
    fn read_large_buffer() {
        let (reader, mut writer) = pipe().unwrap();
        let mut buf = vec![];
        let count_bytes = 9000;
        let data = Arc::new(repeated_bytes(42, count_bytes));

        {
            let data = Arc::clone(&data);
            spawn(move || {
                assert!(writer.write_all(&data).is_ok());
            });
        }

        let bytes_read = reader.read_to_end(&mut buf).unwrap();
        assert_eq!(bytes_read, count_bytes);
        assert_eq!(buf, *data);
    }

    #[test]
    fn read_to_end() {
        let (reader, mut writer) = pipe().unwrap();
        let mut buf = vec![];

        spawn(move || {
            let delay = Duration::from_millis(200);
            sleep(delay);
            assert!(writer.write_all(&[42]).is_ok());
            sleep(delay);
            assert!(writer.write_all(&[44, 48]).is_ok());
        });

        let bytes_read = reader.read_to_end(&mut buf).unwrap();
        assert_eq!(bytes_read, 3);
        assert_eq!(buf, vec![42, 44, 48]);
    }

    #[test]
    fn read_broken_pipe() {
        let (reader, writer) = pipe().unwrap();
        let mut buf = vec![];

        // Reading the dropped pipe isn't treated as an error.
        drop(writer);
        assert!(reader.read(&mut buf).is_ok());
        assert!(reader.read_to_end(&mut buf).is_ok());
    }

    #[test]
    fn read_into_growing_buffer() {
        let (reader, mut writer) = pipe().unwrap();

        let (tx_writer, rx_writer) = mpsc::channel();
        spawn(move || {
            while let Ok((val, count)) = rx_writer.recv() {
                let data = repeated_bytes(val, count);
                assert!(writer.write_all(&data).is_ok());
            }
        });

        let mut expected = vec![];
        let mut buf = vec![];
        // The range here is mostly arbitrary - intended to not use too much memory or time,
        // but also cover the blocking aspect of pipes (assumed 4k buffer).
        for count in 1000..6000 {
            let val = count as u8;
            assert!(tx_writer.send((val, count)).is_ok());
            let mut bytes_read = 0usize;
            while bytes_read < count {
                bytes_read += reader.read(&mut buf).unwrap();
            }
            expected.append(&mut repeated_bytes(val, count));
            assert_eq!(buf, expected);
        }
    }
}
