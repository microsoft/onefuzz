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

#[test]
fn test_module_filter_def_include_filter() {
    let text = r#"{ "module": "abc.exe", "include": [] }"#;
    let def: ModuleRuleDef = serde_json::from_str(text).unwrap();
    assert_eq!(def.module, "abc.exe");
    assert!(matches!(def.rule, RuleDef::Filter(Filter::Include(_))));

    let text = r#"{ "module": "abc.exe", "include": [ "^parse_data$" ] }"#;
    let def: ModuleRuleDef = serde_json::from_str(text).unwrap();
    assert_eq!(def.module, "abc.exe");
    assert!(matches!(def.rule, RuleDef::Filter(Filter::Include(_))));
}

#[test]
fn test_module_filter_def_exclude_filter() {
    let text = r#"{ "module": "abc.exe", "exclude": [] }"#;
    let def: ModuleRuleDef = serde_json::from_str(text).unwrap();
    assert_eq!(def.module, "abc.exe");
    assert!(matches!(def.rule, RuleDef::Filter(Filter::Exclude(_))));

    let text = r#"{ "module": "abc.exe", "exclude": [ "^parse_data$" ] }"#;
    let def: ModuleRuleDef = serde_json::from_str(text).unwrap();
    assert_eq!(def.module, "abc.exe");
    assert!(matches!(def.rule, RuleDef::Filter(Filter::Exclude(_))));
}