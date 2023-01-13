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
