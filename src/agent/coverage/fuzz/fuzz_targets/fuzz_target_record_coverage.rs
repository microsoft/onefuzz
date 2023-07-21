#![no_main]

use libfuzzer_sys::fuzz_target;
use std::env;
use std::fs;
use std::io::Write;
use std::process::Command;
use std::sync::Arc;
use std::time::Duration;

use tempfile::NamedTempFile;

use coverage::allowlist::TargetAllowList;
use coverage::binary::BinaryCoverage;
use coverage::record::CoverageRecorder;

use debuggable_module::loader::Loader;

const INPUT_MARKER: &str = "@@";

fuzz_target!(|data: &[u8]| {
    if data.len() == 0 {
        return;
    }

    // Write mutated bytes to a file
    let mut file = NamedTempFile::new_in(env::current_dir().unwrap()).unwrap();
    file.write_all(data);
    let path = String::from(file.path().to_str().unwrap());

    // Make sure the file is executable
    Command::new("chmod").args(["+wrx", &path]).spawn().unwrap();
    file.keep().unwrap();

    let timeout = Duration::from_secs(5);

    let allowlist = TargetAllowList::default();

    let _coverage = BinaryCoverage::default();
    let loader = Arc::new(Loader::new());

    let cmd = Command::new(&path);

    let _recorded = CoverageRecorder::new(cmd)
        .allowlist(allowlist.clone())
        .loader(loader)
        .timeout(timeout)
        .record();

    fs::remove_file(path);
});
