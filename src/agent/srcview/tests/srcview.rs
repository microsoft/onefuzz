// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
fn path() {
    let srcview = test_srcview();

    let good: Vec<usize> = srcview
        .path_lines("E:\\1f\\coverage\\example\\example.c")
        .unwrap()
        .collect();

    assert_eq!(good, vec![3, 4, 5, 6, 7, 10, 11]);

    assert!(srcview.path_lines("z:\\does\\not\\exist.c").is_none());
}
