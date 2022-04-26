// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::HashMap;
use std::path::Path;

use anyhow::{bail, Result};

pub fn default_llvm_symbolizer_path() -> Result<String> {
    Ok(std::env::var("LLVM_SYMBOLIZER_PATH")?)
}

pub fn default_sanitizer_env_vars() -> Result<HashMap<String, String>> {
    Ok(LlvmSymbolizer::from_env()?.sanitizer_env_vars())
}

/// Valid path to an `llvm-symbolizer` executable that can be used as an
/// external symbolizer in sanitizer environment variables and options.
#[derive(Clone, Debug)]
pub struct LlvmSymbolizer {
    path: String,
}

impl LlvmSymbolizer {
    pub fn new(path: impl Into<String>) -> Result<Self> {
        let path = path.into();

        if !is_valid_symbolizer_path(&path) {
            bail!("invalid symbolizer path: {}", path)
        }

        Ok(Self { path })
    }

    pub fn from_env() -> Result<Self> {
        let path = default_llvm_symbolizer_path()?;

        Self::new(path)
    }

    pub fn as_str(&self) -> &str {
        &self.path
    }

    pub fn sanitizer_env_vars(&self) -> HashMap<String, String> {
        let mut env = HashMap::default();

        env.insert("ASAN_SYMBOLIZER_PATH".to_owned(), self.path.clone());

        let options = format!("external_symbolizer_path={}", self.path);
        env.insert("TSAN_OPTIONS".to_owned(), options);

        env
    }
}

impl From<LlvmSymbolizer> for String {
    fn from(symbolizer: LlvmSymbolizer) -> String {
        symbolizer.path
    }
}

impl AsRef<str> for LlvmSymbolizer {
    fn as_ref(&self) -> &str {
        &self.path
    }
}

fn is_valid_symbolizer_path(symbolizer: &str) -> bool {
    let file_name = Path::new(&symbolizer).file_name().and_then(|n| n.to_str());

    match file_name {
        Some("llvm-symbolizer") => {
            // Always valid.
            true
        }
        #[cfg(target_os = "windows")]
        Some("llvm-symbolizer.exe") => {
            // Valid on Windows only.
            true
        }
        _ => {
            // No other file name is valid.
            false
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_sanitizer_llvm_symbolizer_validation() -> Result<()> {
        // Always valid.
        assert!(LlvmSymbolizer::new("/my/llvm-symbolizer").is_ok());

        #[cfg(target_os = "linux")]
        {
            // Invalid on Linux: extension not allowed.
            assert!(LlvmSymbolizer::new("/my/llvm-symbolizer.exe").is_err());
        }

        #[cfg(target_os = "windows")]
        {
            // Valid on Windows.
            assert!(LlvmSymbolizer::new(r"c:\my\llvm-symbolizer").is_ok());
            assert!(LlvmSymbolizer::new(r"c:\my\llvm-symbolizer.exe").is_ok());
            assert!(LlvmSymbolizer::new("c:/my/llvm-symbolizer.exe").is_ok());

            // Invalid on Windows: extension must be `exe` when present.
            assert!(LlvmSymbolizer::new(r"c:\my\llvm-symbolizer.bin").is_err());

            // Invalid on Windows: extension ok, but file stem must be `llvm-symbolizer`.
            assert!(LlvmSymbolizer::new(r"c:\my\llvm-symbolizer-12.exe").is_err());
        }

        // Invalid: file stem must be `llvm-symbolizer`.
        assert!(LlvmSymbolizer::new("/my/llvm-symbolizer-12").is_err());

        Ok(())
    }

    #[test]
    fn test_sanitizer_env_vars() -> Result<()> {
        const SYMBOLIZER_PATH: &str = "/my/llvm-symbolizer";
        let symbolizer = LlvmSymbolizer::new(SYMBOLIZER_PATH)?;
        let vars = symbolizer.sanitizer_env_vars();

        assert_eq!(vars["ASAN_SYMBOLIZER_PATH"], SYMBOLIZER_PATH);

        let tsan_options = format!("external_symbolizer_path={}", SYMBOLIZER_PATH);
        assert_eq!(vars["TSAN_OPTIONS"], tsan_options);

        assert_eq!(vars.len(), 2);

        Ok(())
    }
}
