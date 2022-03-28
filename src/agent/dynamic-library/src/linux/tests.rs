// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
use super::*;

const LDD_OUTPUT_MISSING_0: &[u8] = include_bytes!("./ldd_output_missing_0.txt");
const LDD_OUTPUT_MISSING_1: &[u8] = include_bytes!("./ldd_output_missing_1.txt");
const LDD_OUTPUT_MISSING_2: &[u8] = include_bytes!("./ldd_output_missing_2.txt");

const LD_DEBUG_OUTPUT_MISSING: &[u8] = include_bytes!("./ld_debug_output_missing.txt");
const LD_DEBUG_OUTPUT_NONE_MISSING: &[u8] = include_bytes!("./ld_debug_output_none_missing.txt");

#[test]
fn test_linked_dynamic_libraries_missing_0() {
    let linked = LinkedDynamicLibraries::parse(LDD_OUTPUT_MISSING_0);

    assert_eq!(linked.libraries.len(), 3);
    assert_eq!(
        linked.libraries["libmycode.so"],
        Some("/my/project/libmycode.so.1".to_owned())
    );
    assert_eq!(
        linked.libraries["libpthread.so.0"],
        Some("/lib/x86_64-linux-gnu/libpthread.so.0".to_owned())
    );
    assert_eq!(
        linked.libraries["libc.so.6"],
        Some("/lib/x86_64-linux-gnu/libc.so.6".to_owned())
    );
}

#[test]
fn test_linked_dynamic_libraries_missing_1() {
    let linked = LinkedDynamicLibraries::parse(LDD_OUTPUT_MISSING_1);

    assert_eq!(linked.libraries.len(), 3);
    assert_eq!(linked.libraries["libmycode.so"], None);
    assert_eq!(
        linked.libraries["libpthread.so.0"],
        Some("/lib/x86_64-linux-gnu/libpthread.so.0".to_owned())
    );
    assert_eq!(
        linked.libraries["libc.so.6"],
        Some("/lib/x86_64-linux-gnu/libc.so.6".to_owned())
    );
}

#[test]
fn test_linked_dynamic_libraries_missing_none() {
    let linked = LinkedDynamicLibraries::parse(LDD_OUTPUT_MISSING_2);

    assert_eq!(linked.libraries.len(), 4);
    assert_eq!(linked.libraries["libmycode.so"], None);
    assert_eq!(linked.libraries["libmyothercode.so"], None);
    assert_eq!(
        linked.libraries["libpthread.so.0"],
        Some("/lib/x86_64-linux-gnu/libpthread.so.0".to_owned())
    );
    assert_eq!(
        linked.libraries["libc.so.6"],
        Some("/lib/x86_64-linux-gnu/libc.so.6".to_owned())
    );
}

#[test]
fn test_ld_debug_logs_parse_missing() {
    let logs = LdDebugLogs::parse(LD_DEBUG_OUTPUT_MISSING);
    let missing = logs.missing();

    assert_eq!(missing.len(), 1);

    let expected = MissingDynamicLibrary {
        name: "libmycode.so".to_owned(),
    };
    assert!(missing.contains(&expected));
}

#[test]
fn test_ld_debug_logs_parse_none_missing() {
    let logs = LdDebugLogs::parse(LD_DEBUG_OUTPUT_NONE_MISSING);
    let missing = logs.missing();

    assert!(missing.is_empty())
}
