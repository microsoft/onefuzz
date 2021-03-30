// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use super::*;

#[test]
fn test_module_filter_def_include_bool() {
    let text = r#"{ "module": "abc.exe", "include": true }"#;
    let def: ModuleRuleDef = serde_json::from_str(text).unwrap();

    assert_eq!(def.module, "abc.exe");
    assert!(matches!(def.rule, RuleDef::Include { include: true }));

    let text = r#"{ "module": "abc.exe", "include": false }"#;
    let def: ModuleRuleDef = serde_json::from_str(text).unwrap();

    assert_eq!(def.module, "abc.exe");
    assert!(matches!(def.rule, RuleDef::Include { include: false }));
}

#[test]
fn test_module_filter_def_exclude_bool() {
    let text = r#"{ "module": "abc.exe", "exclude": true }"#;
    let def: ModuleRuleDef = serde_json::from_str(text).unwrap();

    assert_eq!(def.module, "abc.exe");
    assert!(matches!(def.rule, RuleDef::Exclude { exclude: true }));

    let text = r#"{ "module": "abc.exe", "exclude": false }"#;
    let def: ModuleRuleDef = serde_json::from_str(text).unwrap();

    assert_eq!(def.module, "abc.exe");
    assert!(matches!(def.rule, RuleDef::Exclude { exclude: false }));
}

#[cfg(feature = "symbol-filter")]
#[test]
fn test_module_filter_def_include_filter() {
    let text = r#"{ "module": "abc.exe", "include": [] }"#;
    let def: ModuleRuleDef = serde_json::from_str(text).unwrap();

    assert_eq!(def.module, "abc.exe");

    if let RuleDef::Filter(filter) = def.rule {
        assert!(matches!(*filter, Filter::Include(_)));
    } else {
        panic!("expected a `Filter` rule");
    }

    let text = r#"{ "module": "abc.exe", "include": [ "^parse_data$" ] }"#;
    let def: ModuleRuleDef = serde_json::from_str(text).unwrap();

    assert_eq!(def.module, "abc.exe");

    if let RuleDef::Filter(filter) = def.rule {
        assert!(matches!(*filter, Filter::Include(_)));
    } else {
        panic!("expected a `Filter` rule");
    }
}

#[cfg(feature = "symbol-filter")]
#[test]
fn test_module_filter_def_exclude_filter() {
    let text = r#"{ "module": "abc.exe", "exclude": [] }"#;
    let def: ModuleRuleDef = serde_json::from_str(text).unwrap();

    if let RuleDef::Filter(filter) = def.rule {
        assert!(matches!(*filter, Filter::Exclude(_)));
    } else {
        panic!("expected a `Filter` rule");
    }

    let text = r#"{ "module": "abc.exe", "exclude": [ "^parse_data$" ] }"#;
    let def: ModuleRuleDef = serde_json::from_str(text).unwrap();

    if let RuleDef::Filter(filter) = def.rule {
        assert!(matches!(*filter, Filter::Exclude(_)));
    } else {
        panic!("expected a `Filter` rule");
    }
}

#[cfg(feature = "symbol-filter")]
#[test]
fn test_include_exclude() {
    let include_false = Rule::from(RuleDef::Include { include: false });
    assert!(matches!(include_false, Rule::IncludeModule(false)));

    let exclude_true = Rule::from(RuleDef::Exclude { exclude: true });
    assert!(matches!(exclude_true, Rule::IncludeModule(false)));

    let include_true = Rule::from(RuleDef::Include { include: true });
    assert!(matches!(include_true, Rule::IncludeModule(true)));

    let exclude_false = Rule::from(RuleDef::Exclude { exclude: false });
    assert!(matches!(exclude_false, Rule::IncludeModule(true)));
}

#[cfg(feature = "symbol-filter")]
macro_rules! from_json {
    ($tt: tt) => {{
        let text = stringify!($tt);
        let def: CmdFilterDef =
            serde_json::from_str(text).expect("static test data was invalid JSON");
        CmdFilter::new(def).expect("static test JSON was invalid")
    }};
}

#[cfg(feature = "symbol-filter")]
#[cfg(target_os = "windows")]
const EXE: &str = r"c:\bin\fuzz.exe";

#[cfg(feature = "symbol-filter")]
#[cfg(target_os = "linux")]
const EXE: &str = "/bin/fuzz.exe";

#[cfg(feature = "symbol-filter")]
#[cfg(target_os = "windows")]
const LIB: &str = r"c:\lib\libpthread.dll";

#[cfg(feature = "symbol-filter")]
#[cfg(target_os = "linux")]
const LIB: &str = "/lib/libpthread.so.0";

#[cfg(feature = "symbol-filter")]
fn module(s: &str) -> ModulePath {
    ModulePath::new(s.into()).unwrap()
}

#[cfg(feature = "symbol-filter")]
#[test]
fn test_cmd_filter_empty_def() {
    let filter = from_json!([]);

    // All modules and symbols are included by default.

    let exe = module(EXE);
    assert!(filter.includes_module(&exe));
    assert!(filter.includes_symbol(&exe, "main"));
    assert!(filter.includes_symbol(&exe, "_start"));
    assert!(filter.includes_symbol(&exe, "LLVMFuzzerTestOneInput"));
    assert!(filter.includes_symbol(&exe, "__asan_memcpy"));
    assert!(filter.includes_symbol(&exe, "__asan_load8"));

    let lib = module(LIB);
    assert!(filter.includes_module(&lib));
    assert!(filter.includes_symbol(&lib, "pthread_join"));
    assert!(filter.includes_symbol(&lib, "pthread_yield"));
}

#[cfg(feature = "symbol-filter")]
#[test]
fn test_cmd_filter_module_include_list() {
    let filter = from_json!([
        {
            "module": "fuzz.exe$",
            "include": ["^main$", "LLVMFuzzerTestOneInput"]
        }
    ]);

    // The filtered module and its matching symbols are included.
    let exe = module(EXE);
    assert!(filter.includes_module(&exe));
    assert!(!filter.includes_symbol(&exe, "_start"));
    assert!(filter.includes_symbol(&exe, "main"));
    assert!(filter.includes_symbol(&exe, "LLVMFuzzerTestOneInput"));
    assert!(!filter.includes_symbol(&exe, "__asan_memcpy"));
    assert!(!filter.includes_symbol(&exe, "__asan_load8"));

    // Other modules and their symbols are included by default.
    let lib = module(LIB);
    assert!(filter.includes_module(&lib));
    assert!(filter.includes_symbol(&lib, "pthread_join"));
    assert!(filter.includes_symbol(&lib, "pthread_yield"));
    assert!(filter.includes_symbol(&lib, "__asan_memcpy"));
    assert!(filter.includes_symbol(&lib, "__asan_load8"));
}

#[cfg(feature = "symbol-filter")]
#[test]
fn test_cmd_filter_exclude_list() {
    let filter = from_json!([
        {
            "module": "fuzz.exe$",
            "exclude": ["^_start", "^__asan"]
        }
    ]);

    // The filtered module is included, and its matching symbols are excluded.
    let exe = module(EXE);
    assert!(filter.includes_module(&exe));
    assert!(!filter.includes_symbol(&exe, "_start"));
    assert!(filter.includes_symbol(&exe, "main"));
    assert!(filter.includes_symbol(&exe, "LLVMFuzzerTestOneInput"));
    assert!(!filter.includes_symbol(&exe, "__asan_memcpy"));
    assert!(!filter.includes_symbol(&exe, "__asan_load8"));
    assert!(!filter.includes_symbol(&exe, "_start"));

    // Other modules and their symbols are included by default.
    let lib = module(LIB);
    assert!(filter.includes_module(&lib));
    assert!(filter.includes_symbol(&lib, "pthread_join"));
    assert!(filter.includes_symbol(&lib, "pthread_yield"));
    assert!(filter.includes_symbol(&lib, "__asan_memcpy"));
    assert!(filter.includes_symbol(&lib, "__asan_load8"));
}

#[cfg(feature = "symbol-filter")]
#[test]
fn test_cmd_filter_include_list_and_exclude_default() {
    // The 2nd rule in this list excludes all modules and symbols not explicitly
    // included in the 1st rule.
    let filter = from_json!([
        {
            "module": "fuzz.exe$",
            "include": ["^main$", "LLVMFuzzerTestOneInput"]
        },
        {
            "module": ".*",
            "exclude": true
        }
    ]);

    // The filtered module is included, and only matching rules are included.
    let exe = module(EXE);
    assert!(filter.includes_module(&exe));
    assert!(!filter.includes_symbol(&exe, "_start"));
    assert!(filter.includes_symbol(&exe, "main"));
    assert!(filter.includes_symbol(&exe, "LLVMFuzzerTestOneInput"));
    assert!(!filter.includes_symbol(&exe, "__asan_memcpy"));
    assert!(!filter.includes_symbol(&exe, "__asan_load8"));

    // Other modules and their symbols are excluded by default.
    let lib = module(LIB);
    assert!(!filter.includes_module(&lib));
    assert!(!filter.includes_symbol(&lib, "pthread_yield"));
    assert!(!filter.includes_symbol(&lib, "pthread_join"));
    assert!(!filter.includes_symbol(&lib, "__asan_memcpy"));
    assert!(!filter.includes_symbol(&lib, "__asan_load8"));
}
