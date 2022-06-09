// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::cmp::Ordering;
use std::error::Error;
use std::fmt;

use log::*;

use nom::bytes::complete::{tag, take_till1, take_while};
use nom::character::complete::line_ending;
use nom::combinator::{eof, map_res, opt};
use nom::multi::many0;
use nom::IResult;

/// A module name and an offset
#[derive(Clone, Eq, PartialEq, Hash)]
pub struct ModOff {
    pub module: String,
    pub offset: usize,
}

#[derive(Clone, Copy, Debug, Eq, PartialEq, Hash)]
pub enum ModOffParseError {
    InvalidFormat,
}

impl From<nom::Err<nom::error::Error<&str>>> for ModOffParseError {
    fn from(_: nom::Err<nom::error::Error<&str>>) -> Self {
        ModOffParseError::InvalidFormat
    }
}

impl Error for ModOffParseError {
    fn source(&self) -> Option<&(dyn Error + 'static)> {
        // TODO percolate up the nom error...
        None
    }
}

impl fmt::Display for ModOffParseError {
    fn fmt(&self, fmt: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(fmt, "invalid modoff")
    }
}

impl fmt::Debug for ModOff {
    fn fmt(&self, fmt: &mut fmt::Formatter) -> fmt::Result {
        fmt.debug_struct("ModOff")
            .field("module", &self.module)
            .field("offset", &format_args!("{:#x}", self.offset))
            .finish()
    }
}

impl fmt::Display for ModOff {
    fn fmt(&self, fmt: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(fmt, "{}+{:x}", &self.module, self.offset)
    }
}

impl Ord for ModOff {
    fn cmp(&self, other: &Self) -> Ordering {
        let path_cmp = self.module.cmp(&other.module);

        if path_cmp != Ordering::Equal {
            return path_cmp;
        }

        self.offset.cmp(&other.offset)
    }
}

impl PartialOrd for ModOff {
    fn partial_cmp(&self, other: &Self) -> Option<Ordering> {
        Some(self.cmp(other))
    }
}

impl ModOff {
    pub fn new(module: &str, offset: usize) -> Self {
        Self {
            module: module.to_owned(),
            offset,
        }
    }

    fn parse_module(input: &str) -> IResult<&str, String> {
        let (input, module) = take_till1(|c| c == '+')(input)?;

        Ok((input, module.to_owned()))
    }

    fn from_hex(input: &str) -> Result<usize, std::num::ParseIntError> {
        usize::from_str_radix(input, 16)
    }

    fn is_hex_digit(c: char) -> bool {
        c.is_ascii_hexdigit()
    }

    fn parse_offset(input: &str) -> IResult<&str, usize> {
        let (input, _) = opt(tag("0x"))(input)?;
        map_res(take_while(Self::is_hex_digit), Self::from_hex)(input)
    }

    fn parse_modoff(input: &str) -> IResult<&str, Self> {
        // TODO add modoff comment support -- lines starting with # or ;
        let (input, module) = Self::parse_module(input)?;
        let (input, _) = tag("+")(input)?;
        let (input, offset) = Self::parse_offset(input)?;
        let (input, _) = opt(line_ending)(input)?;

        Ok((input, Self { module, offset }))
    }

    /// Parse a newline separate string of modoffs to a `Vec`
    ///
    /// # Arguments
    ///
    /// * `input` - A string containing new line separated '<module>+<hex offset>'
    ///
    /// # Errors
    ///
    /// If the input string contains invalid module+offset
    ///
    /// # Example
    /// ```
    /// use srcview::ModOff;
    ///
    /// assert_eq!(
    ///     vec![
    ///         ModOff::new("foo.exe", 0x4141),
    ///         ModOff::new("foo.exe", 0x4242)
    ///     ],
    ///     ModOff::parse("foo.exe+4141\nfoo.exe+4242").unwrap()
    /// );
    /// ```
    pub fn parse(input: &str) -> Result<Vec<Self>, ModOffParseError> {
        let (input, res) = many0(Self::parse_modoff)(input)?;
        let (_, _) = eof(input)?;

        info!("parsed {} modoff entries", res.len());

        Ok(res)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use anyhow::Result;

    #[test]
    fn parse_empty() -> Result<()> {
        let empty: Vec<ModOff> = Vec::new();
        assert_eq!(empty, ModOff::parse("")?);
        Ok(())
    }

    #[test]
    fn parse_good() -> Result<()> {
        assert_eq!(
            vec![ModOff::new("foo.exe", 0x4141)],
            ModOff::parse("foo.exe+4141")?
        );
        Ok(())
    }

    #[test]
    fn parse_good_multiple_unix() -> Result<()> {
        assert_eq!(
            vec![
                ModOff::new("foo.exe", 0x4141),
                ModOff::new("foo.exe", 0x4242)
            ],
            ModOff::parse("foo.exe+4141\nfoo.exe+4242")?
        );
        Ok(())
    }

    #[test]
    fn parse_good_multiple_windows() -> Result<()> {
        assert_eq!(
            vec![
                ModOff::new("foo.exe", 0x4141),
                ModOff::new("foo.exe", 0x4242),
            ],
            ModOff::parse("foo.exe+4141\r\nfoo.exe+4242")?
        );
        Ok(())
    }

    #[test]
    fn parse_good_leading_0x() -> Result<()> {
        assert_eq!(
            vec![ModOff::new("foo.exe", 0x4141)],
            ModOff::parse("foo.exe+0x4141")?
        );
        Ok(())
    }

    #[test]
    fn parse_bad_no_module() {
        assert_eq!(Err(ModOffParseError::InvalidFormat), ModOff::parse("+4141"));
    }
    #[test]
    fn parse_bad_no_plus() {
        assert_eq!(
            Err(ModOffParseError::InvalidFormat),
            ModOff::parse("foo.exe4141")
        );
    }
    #[test]
    fn parse_bad_no_digits() {
        assert_eq!(
            Err(ModOffParseError::InvalidFormat),
            ModOff::parse("foo.exe+")
        );
    }

    #[test]
    fn parse_bad_bad_digits() {
        assert_eq!(
            Err(ModOffParseError::InvalidFormat),
            ModOff::parse("foo.exe+41zz")
        );
    }
}
