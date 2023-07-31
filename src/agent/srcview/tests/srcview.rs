// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// tests depends on example.pdb
// $ sha256sum example.pdb
// ecc4214d687c97e9c8afd0c84b4b75383eaa0a237f8a8ca5049478f63b2c98b9  example.pdb

use std::env;
use std::path::PathBuf;

use srcview::{ModOff, SrcLine, SrcView};

fn test_srcview() -> SrcView {
    let root = env::var("CARGO_MANIFEST_DIR").unwrap();
    let pdb_path: PathBuf = [&root, "res", "example.pdb"].iter().collect();

    let mut srcview = SrcView::new();
    srcview.insert("example.exe", pdb_path).unwrap();

    srcview
}

#[test]
#[cfg_attr(not(feature = "binary-tests"), ignore)]
fn modoff() {
    let srcview = test_srcview();

    let good_modoff = ModOff::new("example.exe", 0x6f70);
    assert_eq!(
        srcview.modoff(&good_modoff),
        Some(SrcLine::new("E:\\1f\\coverage\\example\\example.c", 3))
    );

    let bad_offset = ModOff::new("example.exe", 0x4141);
    assert_eq!(srcview.modoff(&bad_offset), None);

    let bad_module = ModOff::new("foo.exe", 0x4141);
    assert_eq!(srcview.modoff(&bad_module), None);
}

#[test]
#[cfg_attr(not(feature = "binary-tests"), ignore)]
fn symbol() {
    let srcview = test_srcview();

    let good: Vec<&SrcLine> = srcview.symbol("example.exe!main").unwrap().collect();

    assert_eq!(
        good,
        vec![
            &SrcLine::new("E:\\1f\\coverage\\example\\example.c", 3),
            &SrcLine::new("E:\\1f\\coverage\\example\\example.c", 4),
            &SrcLine::new("E:\\1f\\coverage\\example\\example.c", 5),
            &SrcLine::new("E:\\1f\\coverage\\example\\example.c", 6),
            &SrcLine::new("E:\\1f\\coverage\\example\\example.c", 7),
            &SrcLine::new("E:\\1f\\coverage\\example\\example.c", 10),
            &SrcLine::new("E:\\1f\\coverage\\example\\example.c", 11),
        ]
    );

    assert!(srcview.symbol("dosenotexist").is_none());
}

#[test]
#[cfg_attr(not(feature = "binary-tests"), ignore)]
fn path() {
    let srcview = test_srcview();

    let good: Vec<usize> = srcview
        .path_lines("E:\\1f\\coverage\\example\\example.c")
        .unwrap()
        .collect();

    assert_eq!(good, vec![3, 4, 5, 6, 7, 10, 11]);

    assert!(srcview.path_lines("z:\\does\\not\\exist.c").is_none());
}

#[test]
//#[cfg(target_os = "windows")]
fn windows_snapshot_tests() {
    insta::glob!("testdata", "*.cpp", |path| {
        let output = std::process::Command::new("cl.exe")
            .args("/EHsc")
            .arg("/Zi")
            .arg(path)
            .spawn()
            .expect("launching compiler")
            .wait_with_output()
            .expect("waiting for compiler to finish");

        assert!(output.status.success());

        let exe_name = PathBuf::from(path.file_name().unwrap()).with_extension("exe");
        let pdb_name = exe_name.with_extension("pdb");

        let mut srcview = SrcView::new();
        srcview
            .insert(exe_name.to_string_lossy().as_ref(), &pdb_name)
            .unwrap();

        let exe_cmd = std::process::Command::new(exe_name);
        let recorded = coverage::CoverageRecorder::new(exe_cmd).record().unwrap();

        let mut srclines_hit: Vec<SrcLine> = Vec::new();
        for (module, coverage) in &recorded.coverage.modules {
            for offset in coverage.as_ref().keys() {
                if let Some(line) = srcview.modoff(&ModOff {
                    module: module.to_string(),
                    offset: offset.0 as usize,
                }) {
                    srclines_hit.push(line);
                }
            }
        }

        insta::assert_json_snapshot!(srclines_hit);
    });
}
