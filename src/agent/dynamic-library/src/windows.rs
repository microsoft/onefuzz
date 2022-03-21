// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::convert::TryFrom;
use std::fmt;
use std::io;
use std::path::Path;
use std::process::Command;

use debugger::{DebugEventHandler, Debugger};
use lazy_static::lazy_static;
use regex::Regex;
use thiserror::Error;

#[derive(Debug, Error)]
pub enum CheckDynamicLibrariesError {
    #[error("invalid image file name")]
    ImageFile(#[from] ImageFileError),

    #[error("error accessing image global flags")]
    ImageGlobalFlags(#[from] ImageGlobalFlagsError),

    #[error("debugger error")]
    Debugger(anyhow::Error),
}

pub fn find_missing(
    cmd: Command,
) -> Result<Vec<MissingDynamicLibrary>, CheckDynamicLibrariesError> {
    let image_file = ImageFile::new(cmd.get_program())?;
    let _sls = image_file.show_loader_snaps()?;

    let mut handler = LoaderSnapsHandler::default();

    let (mut dbg, _child) =
        Debugger::init(cmd, &mut handler).map_err(CheckDynamicLibrariesError::Debugger)?;

    while !dbg.target().exited() {
        if !dbg
            .process_event(&mut handler, 1000)
            .map_err(CheckDynamicLibrariesError::Debugger)?
        {
            break;
        }

        dbg.continue_debugging()
            .map_err(CheckDynamicLibrariesError::Debugger)?;
    }

    Ok(handler.missing_libraries())
}

#[derive(Debug, Error)]
pub enum ImageFileError {
    #[error("image file name is not valid utf-8")]
    InvalidEncoding,

    #[error("path to image file missing file name")]
    MissingFileName,
}

/// Name of an image file, for setting global flags.
#[derive(Clone, Debug)]
pub struct ImageFile {
    name: String,
}

impl ImageFile {
    /// Construct a validated image file name from its name or path.
    pub fn new(path: impl AsRef<Path>) -> Result<Self, ImageFileError> {
        Self::try_from(path.as_ref())
    }

    /// Enable loader snap for the image file.
    ///
    /// Requires elevation.
    ///
    /// See: https://docs.microsoft.com/en-us/windows-hardware/drivers/debugger/show-loader-snaps
    pub fn show_loader_snaps(&self) -> Result<ImageLoaderSnapsGuard, ImageGlobalFlagsError> {
        ImageLoaderSnapsGuard::new(self.clone())
    }
}

// Cannot impl for all `AsRef<Path>` due to overlapping blanket impl.
//
// See: rust-lang/rust#50133
impl TryFrom<&Path> for ImageFile {
    type Error = ImageFileError;

    fn try_from(path: &Path) -> Result<Self, Self::Error> {
        let name = path.file_name().ok_or(ImageFileError::MissingFileName)?;
        let name = name
            .to_str()
            .ok_or(ImageFileError::InvalidEncoding)?
            .to_owned();

        Ok(ImageFile { name })
    }
}

impl fmt::Display for ImageFile {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        write!(f, "{}", self.name)
    }
}

#[derive(Debug, Error)]
#[error("error ")]
pub enum ImageGlobalFlagsError {
    #[error("could not create registry key for `{image}`")]
    CreateKey { image: ImageFile, source: io::Error },

    #[error("could not access `GlobalFlag` value of registry key for `{image}`")]
    AccessValue { image: ImageFile, source: io::Error },
}

const GFLAGS_KEY_NAME: &str = "GlobalFlag"; // Singular
const GFLAGS_SHOW_LOADER_SNAPS: u32 = 0x2;

/// The global flags for an image file.
///
/// See: https://docs.microsoft.com/en-us/windows-hardware/drivers/debugger/gflags-overview
struct ImageGlobalFlags {
    image: ImageFile,
}

impl ImageGlobalFlags {
    pub fn new(image: ImageFile) -> Self {
        Self { image }
    }

    pub fn get_value(&self) -> Result<u32, ImageGlobalFlagsError> {
        let value = self
            .create_key()?
            .get_value(GFLAGS_KEY_NAME)
            .map_err(|source| ImageGlobalFlagsError::AccessValue {
                source,
                image: self.image.clone(),
            })?;

        Ok(value)
    }

    pub fn set_value(&self, value: u32) -> Result<(), ImageGlobalFlagsError> {
        self.create_key()?
            .set_value(GFLAGS_KEY_NAME, &value)
            .map_err(|source| ImageGlobalFlagsError::AccessValue {
                source,
                image: self.image.clone(),
            })?;

        Ok(())
    }

    /// Create a registry key to set global flags for the image file.
    fn create_key(&self) -> Result<winreg::RegKey, ImageGlobalFlagsError> {
        use winreg::enums::HKEY_LOCAL_MACHINE;
        use winreg::RegKey;

        let key_name = {
            let mut name = String::from(
                r"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\",
            );
            name.push_str(&self.image.name);
            name
        };

        let hklm = RegKey::predef(HKEY_LOCAL_MACHINE);
        let (key, _key_disposition) =
            hklm.create_subkey(&key_name)
                .map_err(|source| ImageGlobalFlagsError::CreateKey {
                    source,
                    image: self.image.clone(),
                })?;

        Ok(key)
    }
}

pub struct ImageLoaderSnapsGuard {
    /// Image file for which loader snaps should be enabled.
    gflags: ImageGlobalFlags,
}

impl ImageLoaderSnapsGuard {
    pub fn new(image: ImageFile) -> Result<Self, ImageGlobalFlagsError> {
        let gflags = ImageGlobalFlags::new(image);
        let guard = Self { gflags };

        guard.enable()?;

        Ok(guard)
    }

    fn enable(&self) -> Result<(), ImageGlobalFlagsError> {
        let mut value = self.gflags.get_value()?;
        value |= GFLAGS_SHOW_LOADER_SNAPS;
        self.gflags.set_value(value)?;

        Ok(())
    }

    fn disable(&self) -> Result<(), ImageGlobalFlagsError> {
        let mut value = self.gflags.get_value()?;
        value &= !GFLAGS_SHOW_LOADER_SNAPS;
        self.gflags.set_value(value)?;

        Ok(())
    }
}

impl Drop for ImageLoaderSnapsGuard {
    fn drop(&mut self) {
        let _ = self.disable();
    }
}

#[derive(Clone, Debug, Eq, PartialEq)]
pub struct MissingDynamicLibrary {
    pub name: String,
    pub parent: String,
    pub status: u32,
}

impl MissingDynamicLibrary {
    pub fn parse(text: &str) -> Option<Self> {
        let captures = MISSING_DLL_RE.captures(text)?;

        let name = captures.get(1)?.as_str().to_owned();
        let parent = captures.get(2)?.as_str().to_owned();
        let status = u32::from_str_radix(captures.get(3)?.as_str(), 16).ok()?;

        Some(Self {
            name,
            parent,
            status,
        })
    }
}

#[derive(Default)]
pub struct LoaderSnapsHandler {
    pub debug_strings: Vec<String>,
}

impl LoaderSnapsHandler {
    pub fn missing_libraries(&self) -> Vec<MissingDynamicLibrary> {
        let mut missing = vec![];

        for text in &self.debug_strings {
            if let Some(lib) = MissingDynamicLibrary::parse(text) {
                missing.push(lib);
            }
        }

        missing
    }
}

impl DebugEventHandler for LoaderSnapsHandler {
    fn on_output_debug_string(&mut self, _debugger: &mut Debugger, message: String) {
        self.debug_strings.push(message);
    }
}

lazy_static! {
    static ref MISSING_DLL_RE: Regex = Regex::new(
        r#"[0-9a-f]+:[0-9a-f]+ @ [0-9a-f]+ - LdrpProcessWork - ERROR: Unable to load DLL: "(.+)", Parent Module: "(.+)", Status: 0x([0-9a-f]+)"#
    ).unwrap();
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_missing_dynamic_library_parse_none() {
        // Broad error message about failed process init.
        const NOT_MISSING_TEXT: &str = "7c48:2ac4 @ 371984000 - LdrpInitializationFailure - ERROR: Process initialization failed with status 0xc0000135";

        let not_missing = MissingDynamicLibrary::parse(NOT_MISSING_TEXT);

        assert!(not_missing.is_none());
    }

    #[test]
    fn test_missing_dynamic_library_parse_some() {
        // Specific error message that tells us the missing library.
        const MISSING_TEXT: &str = r#"7c48:57c8 @ 371984000 - LdrpProcessWork - ERROR: Unable to load DLL: "lost.dll", Parent Module: "C:\my\project\fuzz.exe", Status: 0xc0000135"#;

        let missing =
            MissingDynamicLibrary::parse(MISSING_TEXT).expect("failed to parse missing DLL");

        assert_eq!(missing.name, "lost.dll");
        assert_eq!(missing.parent, r"C:\my\project\fuzz.exe");
        assert_eq!(missing.status, 0xc0000135);
    }
}
