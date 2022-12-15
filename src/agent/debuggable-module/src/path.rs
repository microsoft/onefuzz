// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::ffi::OsStr;
use std::fmt;
use std::path::{Path, PathBuf};

use anyhow::{bail, Result};

/// Path to a file. Guaranteed UTF-8.
#[derive(Clone, Debug, Eq, Hash, Ord, PartialEq, PartialOrd)]
pub struct FilePath(String);

impl FilePath {
    pub fn new(path: impl Into<String>) -> Result<Self> {
        let path = path.into();

        if Path::new(&path).file_name().is_none() {
            bail!("module path has no file name");
        }

        if Path::new(&path).file_stem().is_none() {
            bail!("module path has no file stem");
        }

        Ok(Self(path))
    }

    pub fn with_extension(&self, extension: impl AsRef<str>) -> Self {
        let path = self
            .as_path()
            .with_extension(extension.as_ref())
            .to_string_lossy()
            .into_owned();

        Self(path)
    }

    pub fn as_path(&self) -> &Path {
        Path::new(&self.0)
    }

    pub fn as_str(&self) -> &str {
        &self.0
    }

    pub fn file_name(&self) -> &str {
        // Unwraps checked by ctor.
        Path::new(&self.0).file_name().unwrap().to_str().unwrap()
    }

    pub fn directory(&self) -> &str {
        // Unwraps checked by ctor.
        Path::new(&self.0).parent().unwrap().to_str().unwrap()
    }

    pub fn base_name(&self) -> &str {
        // Unwraps checked by ctor.
        Path::new(&self.0).file_stem().unwrap().to_str().unwrap()
    }
}

impl From<FilePath> for String {
    fn from(path: FilePath) -> Self {
        path.0
    }
}
impl From<FilePath> for PathBuf {
    fn from(path: FilePath) -> Self {
        path.0.into()
    }
}

impl AsRef<str> for FilePath {
    fn as_ref(&self) -> &str {
        self.as_str()
    }
}

impl AsRef<OsStr> for FilePath {
    fn as_ref(&self) -> &OsStr {
        self.as_str().as_ref()
    }
}

impl AsRef<Path> for FilePath {
    fn as_ref(&self) -> &Path {
        self.as_path()
    }
}

impl fmt::Display for FilePath {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        write!(f, "{}", self.0)
    }
}
