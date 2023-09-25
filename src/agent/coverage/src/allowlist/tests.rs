use anyhow::Result;

use super::AllowList;

#[test]
fn test_default() -> Result<()> {
    let allowlist = AllowList::default();

    // All allowed.
    assert!(allowlist.is_allowed("a"));
    assert!(allowlist.is_allowed("a/b"));
    assert!(allowlist.is_allowed("b"));
    assert!(allowlist.is_allowed("c"));

    Ok(())
}

#[test]
fn test_empty() -> Result<()> {
    let text = include_str!("test-data/empty.txt");
    let allowlist = AllowList::parse(text)?;

    // All excluded.
    assert!(!allowlist.is_allowed("a"));
    assert!(!allowlist.is_allowed("a/b"));
    assert!(!allowlist.is_allowed("b"));
    assert!(!allowlist.is_allowed("c"));

    Ok(())
}

#[test]
fn test_allow_some() -> Result<()> {
    let text = include_str!("test-data/allow-some.txt");
    let allowlist = AllowList::parse(text)?;

    assert!(allowlist.is_allowed("a"));
    assert!(!allowlist.is_allowed("a/b"));
    assert!(allowlist.is_allowed("b"));
    assert!(!allowlist.is_allowed("c"));

    Ok(())
}

#[test]
fn test_allow_all() -> Result<()> {
    let text = include_str!("test-data/allow-all.txt");
    let allowlist = AllowList::parse(text)?;

    assert!(allowlist.is_allowed("a"));
    assert!(allowlist.is_allowed("a/b"));
    assert!(allowlist.is_allowed("b"));
    assert!(allowlist.is_allowed("c"));

    Ok(())
}

#[test]
fn test_allow_all_glob() -> Result<()> {
    let text = include_str!("test-data/allow-all-glob.txt");
    let allowlist = AllowList::parse(text)?;

    assert!(allowlist.is_allowed("a"));
    assert!(allowlist.is_allowed("a/b"));
    assert!(allowlist.is_allowed("b"));
    assert!(allowlist.is_allowed("c"));

    Ok(())
}

#[test]
fn test_allow_glob_except() -> Result<()> {
    let text = include_str!("test-data/allow-all-glob-except.txt");
    let allowlist = AllowList::parse(text)?;

    assert!(!allowlist.is_allowed("a"));
    assert!(allowlist.is_allowed("a/b"));
    assert!(!allowlist.is_allowed("a/c"));
    assert!(allowlist.is_allowed("a/d"));
    assert!(!allowlist.is_allowed("b"));
    assert!(allowlist.is_allowed("c"));

    Ok(())
}

#[test]
fn test_allow_glob_except_commented() -> Result<()> {
    let text = include_str!("test-data/allow-all-glob-except-commented.txt");
    let allowlist = AllowList::parse(text)?;

    assert!(!allowlist.is_allowed("a"));
    assert!(allowlist.is_allowed("a/b"));
    assert!(!allowlist.is_allowed("a/c"));
    assert!(allowlist.is_allowed("a/d"));
    assert!(!allowlist.is_allowed("b"));

    // Allowed by the rule `c`, but not allowed because `# c` is a comment.
    assert!(!allowlist.is_allowed("c"));

    Ok(())
}

#[test]
fn test_allow_glob_extension() -> Result<()> {
    let text = include_str!("test-data/allow-all-glob-extension.txt");
    let allowlist = AllowList::parse(text)?;

    assert!(allowlist.is_allowed("a.c"));
    assert!(allowlist.is_allowed("a.h"));

    assert!(!allowlist.is_allowed("ac"));
    assert!(!allowlist.is_allowed("ah"));

    assert!(!allowlist.is_allowed("axc"));
    assert!(!allowlist.is_allowed("axh"));

    Ok(())
}

#[test]
fn test_allowlist_extend() -> Result<()> {
    let baseline_text = "! bad/*
other/*";
    let baseline = AllowList::parse(baseline_text)?;

    assert!(!baseline.is_allowed("bad/a"));
    assert!(!baseline.is_allowed("bad/b"));
    assert!(!baseline.is_allowed("good/a"));
    assert!(!baseline.is_allowed("good/b"));
    assert!(!baseline.is_allowed("good/bad/c"));
    assert!(baseline.is_allowed("other/a"));
    assert!(baseline.is_allowed("other/b"));

    let provided_text = "good/*
bad/*
! other/*";
    let provided = AllowList::parse(provided_text)?;

    assert!(provided.is_allowed("bad/a"));
    assert!(provided.is_allowed("bad/b"));
    assert!(provided.is_allowed("good/a"));
    assert!(provided.is_allowed("good/b"));
    assert!(provided.is_allowed("good/bad/c"));
    assert!(!provided.is_allowed("other/a"));
    assert!(!provided.is_allowed("other/b"));

    let extended = baseline.extend(&provided);

    // Deny rules from `baseline` should not be overridden by `provided`, but
    // allow rules should be.
    //
    // A provided allowlist can deny patterns that are baseline-allowed, but
    // cannot allow patterns that are baseline-denied.
    assert!(!extended.is_allowed("bad/a"));
    assert!(!extended.is_allowed("bad/b"));
    assert!(extended.is_allowed("good/a"));
    assert!(extended.is_allowed("good/b"));
    assert!(extended.is_allowed("good/bad/c"));
    assert!(!extended.is_allowed("other/a"));
    assert!(!extended.is_allowed("other/b"));

    Ok(())
}

#[test]
fn test_allowlist_escape() -> Result<()> {
    const GOOD: &str = "good (x[y]) {z}+ %#S";
    const BAD: &str = "bad* a+b @!{ (x)[y]{z}";

    let text = format!("{GOOD}\n! {BAD}");
    let allowlist = AllowList::parse(&text)?;

    assert!(allowlist.is_allowed(GOOD));
    assert!(!allowlist.is_allowed(BAD));

    Ok(())
}

#[cfg(target_os = "windows")]
#[test]
fn test_windows_allowlists_are_not_case_sensitive() -> Result<()> {
    let allowlist = AllowList::parse("vccrt")?;
    assert!(allowlist.is_allowed("VCCRT"));

    Ok(())
}

#[cfg(not(target_os = "windows"))]
#[test]
fn test_linux_allowlists_are_case_sensitive() -> Result<()> {
    let allowlist = AllowList::parse("vccrt")?;
    assert!(!allowlist.is_allowed("VCCRT"));

    Ok(())
}
