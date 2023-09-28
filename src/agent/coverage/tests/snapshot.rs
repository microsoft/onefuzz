// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#[test]
#[cfg(all(target_os = "windows", feature = "slow-tests"))]
fn windows_snapshot_tests() {
    use coverage::{binary::DebugInfoCache, source::Line, AllowList};
    use debuggable_module::path::FilePath;
    use std::fmt::Write;
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
        let source_allowlist =
            AllowList::parse(&input_path.to_string_lossy().to_ascii_lowercase()).unwrap();

        let exe_cmd = std::process::Command::new(&exe_name);
        let recorded = coverage::CoverageRecorder::new(exe_cmd)
            .timeout(Duration::from_secs(120))
            .debuginfo_cache(DebugInfoCache::new(source_allowlist.clone()))
            .record()
            .unwrap();

        // generate source-line coverage info
        let source =
            coverage::source::binary_to_source_coverage(&recorded.coverage, &source_allowlist)
                .expect("binary_to_source_coverage");

        println!("{:?}", source.files.keys());

        // For Windows, the source coverage is tracked using case-insensitive paths.
        // The conversion from case-sensitive to insensitive is done when converting from binary to source coverage.
        // By naming our test file with a capital letter, we can ensure that the case-insensitive conversion is working.
        source.files.keys().for_each(|k| {
            assert_eq!(k.to_string().to_ascii_lowercase(), k.to_string());
        });

        let file_coverage = source
            .files
            .get(&FilePath::new(input_path.to_string_lossy().to_ascii_lowercase()).unwrap())
            .expect("coverage for input");

        let mut result = String::new();

        let file_source = std::fs::read_to_string(input_path).expect("reading source file");
        for (ix, content) in file_source.lines().enumerate() {
            let line = Line::new((ix + 1) as u32).unwrap();
            let prefix = if file_coverage.lines.contains_key(&line) {
                "[✔]"
            } else {
                "[ ]"
            };

            writeln!(result, "{prefix} {content}").unwrap();
        }

        insta::assert_snapshot!(result);
    });
}
