#![no_main]
use libfuzzer_sys::fuzz_target;
use rust_fuzz_example;

fuzz_target!(|data: &[u8]| {
    rust_fuzz_example::check(data);
});
