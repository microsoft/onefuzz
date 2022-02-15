#[cfg(target_os = "windows")]
use std::convert::TryFrom;

use anyhow::Result;

#[cfg(target_os = "linux")]
use regex::Regex;

#[cfg(target_os = "windows")]
use winapi::um::psapi::PERFORMANCE_INFORMATION;

#[cfg(target_os = "windows")]
pub fn available_bytes() -> Result<u64> {
    let info = get_performance_info()?;
    let pages = info.CommitLimit.saturating_sub(info.CommitTotal);
    let bytes = pages * info.PageSize;
    let bytes = u64::try_from(bytes)?;

    Ok(bytes)
}

#[cfg(target_os = "windows")]
fn get_performance_info() -> Result<PERFORMANCE_INFORMATION> {
    use winapi::shared::minwindef::FALSE;
    use winapi::um::errhandlingapi::GetLastError;
    use winapi::um::psapi::GetPerformanceInfo;

    let mut info = PERFORMANCE_INFORMATION::default();

    let success = unsafe {
        // Will always fit in a `u32`.
        //
        // https://docs.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-performance_information
        let size = std::mem::size_of::<PERFORMANCE_INFORMATION>();
        let size = u32::try_from(size)?;
        GetPerformanceInfo(&mut info, size)
    };

    if success == FALSE {
        let code = unsafe { GetLastError() };
        bail!("error querying performance information: {:x}", code);
    }

    Ok(info)
}

#[cfg(target_os = "linux")]
pub fn available_bytes() -> Result<u64> {
    const BYTES_PER_KB: u64 = 1024;

    let meminfo = std::fs::read_to_string("/proc/meminfo")?;
    let available_kb = parse_available_kb(&meminfo)?;
    let available_bytes = available_kb * BYTES_PER_KB;

    Ok(available_bytes)
}

#[cfg(target_os = "linux")]
fn parse_available_kb(meminfo: &str) -> Result<u64> {
    let captures = AVAILABLE_KB
        .captures(&meminfo)
        .ok_or_else(|| format_err!("`MemAvailable` not found in `/proc/meminfo`"))?;

    let available_kb = captures
        .get(1)
        .ok_or_else(|| format_err!("`MemAvailable` not found in `/proc/meminfo`"))?
        .as_str()
        .parse()?;

    Ok(available_kb)
}

#[cfg(target_os = "linux")]
lazy_static::lazy_static! {
    static ref AVAILABLE_KB: Regex = Regex::new(r"MemAvailable:\s*(\d+) kB").unwrap();
}

#[cfg(test)]
#[cfg(target_os = "linux")]
mod tests_linux;
