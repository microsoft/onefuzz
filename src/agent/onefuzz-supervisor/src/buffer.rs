// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
#[derive(Clone, Debug, Eq, PartialEq)]
pub struct TailBuffer {
    data: Vec<u8>,
    capacity: usize,
}

impl TailBuffer {
    pub fn new(capacity: usize) -> Self {
        let data = Vec::with_capacity(capacity);
        Self { data, capacity }
    }

    pub fn data(&self) -> &[u8] {
        &self.data
    }

    pub fn to_string_lossy(&self) -> String {
        String::from_utf8_lossy(self.data()).to_string()
    }
}

impl std::io::Write for TailBuffer {
    fn write(&mut self, new_data: &[u8]) -> std::io::Result<usize> {
        // Write the new data to the internal buffer, allocating internally as needed.
        self.data.extend(new_data);

        // Shift and truncate the buffer if it is too big.
        if self.data.len() > self.capacity {
            let lo = self.data.len() - self.capacity;
            let range = lo..self.data.len();
            self.data.copy_within(range, 0);
            self.data.truncate(self.capacity);
        }

        Ok(new_data.len())
    }

    fn flush(&mut self) -> std::io::Result<()> {
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use std::io::Write;

    use super::*;

    #[test]
    fn test_tail_buffer() {
        let mut buf = TailBuffer::new(5);

        assert!(buf.data().is_empty());

        buf.write(&[1, 2, 3]).unwrap();
        assert_eq!(buf.data(), &[1, 2, 3]);

        buf.write(&[]).unwrap();
        assert_eq!(buf.data(), &[1, 2, 3]);

        buf.write(&[4, 5]).unwrap();
        assert_eq!(buf.data(), &[1, 2, 3, 4, 5]);

        buf.write(&[6, 7, 8]).unwrap();
        assert_eq!(buf.data(), &[4, 5, 6, 7, 8]);

        buf.write(&[9, 10, 11, 12, 13]).unwrap();
        assert_eq!(buf.data(), &[9, 10, 11, 12, 13]);

        buf.write(&[14, 15, 16, 17, 18, 19, 20, 21, 22, 23]).unwrap();
        assert_eq!(buf.data(), &[19, 20, 21, 22, 23]);
    }
}