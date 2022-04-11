// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::HashMap;

use anyhow::Result;

pub fn default_llvm_symbolizer_path() -> Result<String> {
    Ok(std::env::var("LLVM_SYMBOLIZER_PATH")?)
}

pub fn default_sanitizer_env_vars() -> Result<HashMap<String, String>> {
    let mut env = HashMap::default();

    let llvm_symbolizer = default_llvm_symbolizer_path()?;
    env.insert("ASAN_SYMBOLIZER_PATH".to_owned(), llvm_symbolizer);

    Ok(env)
}