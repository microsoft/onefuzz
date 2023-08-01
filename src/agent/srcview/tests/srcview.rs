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
#[cfg(target_os = "windows")]
fn windows_snapshot_tests() {
    use coverage::{binary::DebugInfoCache, AllowList};
    use std::time::Duration;

    insta::glob!("windows", "*.cpp", |path| {
        let file_name = path.file_name().unwrap();

        // locate appropriate compiler
        let mut cl_exe =
            cc::windows_registry::find("x86_64-pc-windows-msvc", "cl.exe").expect("finding cl.exe");

        // path will have \\?\ prefix, cl.exe doesn't like it
        let input_path = dunce::canonicalize(path).unwrap();

        // directory to compile into
        let build_in = tempfile::tempdir().expect("creating tempdir");

        let output = cl_exe
            .arg("/EHsc")
            .arg("/Zi")
            .arg("/O2")
            .arg(&input_path)
            .current_dir(build_in.path())
            .spawn()
            .expect("launching compiler")
            .wait_with_output()
            .expect("waiting for compiler to finish");

        assert!(output.status.success(), "cl.exe failed: {:?}", output);

        let exe_name = {
            let mut cwd = build_in.path().to_path_buf();
            cwd.push(file_name);
            cwd.set_extension("exe");
            cwd
        };

        // filter to just the input test file:
        let source_filter = AllowList::parse(&input_path.to_string_lossy()).unwrap();

        let exe_cmd = std::process::Command::new(&exe_name);
        let recorded = coverage::CoverageRecorder::new(exe_cmd)
            .timeout(Duration::from_secs(120))
            .debuginfo_cache(DebugInfoCache::new(source_filter))
            .record()
            .unwrap();

        // generate information with srcview
        let target_file_name = exe_name.file_name().unwrap().to_string_lossy().into_owned();
        let mut srcview = SrcView::new();
        srcview
            .insert(&target_file_name, &exe_name.with_extension("pdb"))
            .unwrap();

        let mut srclines_hit: Vec<SrcLine> = Vec::new();
        for (module, coverage) in &recorded.coverage.modules {
            let module_name = module.file_name();
            for (offset, count) in &coverage.offsets {
                if count.0 > 0 {
                    if let Some(line) = srcview.modoff(&ModOff {
                        module: module_name.to_string(),
                        offset: offset.0 as usize,
                    }) {
                        srclines_hit.push(line);
                    };
                }
            }
        }

        // apply filter to make output stable, replace prefix of path with "…"
        insta::with_settings!({filters => vec![(r"[A-Z]:.*windows", "…")]}, {
            insta::assert_json_snapshot!(srclines_hit);
        });
    });
}
