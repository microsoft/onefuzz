#[macro_use]
extern crate anyhow;
#[macro_use]
extern crate clap;
#[macro_use]
extern crate onefuzz_telemetry;

pub mod local;
pub mod tasks;
#[cfg(test)]
pub mod config_test_utils;
