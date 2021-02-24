// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::StackEntry;
use anyhow::{bail, Result};
use regex::Regex;

pub(crate) fn parse_asan_call_stack(text: &str) -> Result<Vec<StackEntry>> {
    let mut stack = vec![];
    let mut parsing_stack = false;

    let base = r"\s*#(?P<frame>\d+)\s+0x(?P<address>[0-9a-fA-F]+)\s";
    let entries = &[
        // "module::func(char *args) (/path/to/bin+0x123)"
        r"in (?P<func_1>.*) \((?P<module_path_1>[^+]+)\+0x(?P<module_offset_1>[0-9a-fA-F]+)\)",
        // "in foo /path:16:17"
        r"in (?P<func_2>.*) (?P<file_name_1>[^ ]+):(?P<file_line_1>\d+):(?P<function_offset_1>\d+)",
        // "in foo /path:16"
        r"in (?P<func_3>.*) (?P<file_name_2>[^ ]+):(?P<file_line_2>\d+)",
        // "  (/path/to/bin+0x123)"
        r" \((?P<module_path_2>[^+]+)\+0x(?P<module_offset_2>[0-9a-fA-F]+)\)",
    ];

    let asan_re = format!("^{}(?:{})$", base, entries.join("|"));

    let asan_base = Regex::new(&asan_re).expect("asan regex failed to compile");

    for line in text.lines() {
        let line = line.trim();
        // println!("LINE: {:?}", line);
        let asan_captures = asan_base.captures(line);
        match (parsing_stack, asan_captures) {
            (true, None) => break,
            (false, None) => {
                continue;
            }
            (_, Some(captures)) => {
                parsing_stack = true;

                // the base capture always matches
                let line = captures[0].to_string();
                // address base capture always matches
                let address = Some(u64::from_str_radix(&captures["address"], 16)?);

                let function_name = captures
                    .name("func_1")
                    .or(captures.name("func_2"))
                    .or(captures.name("func_3"))
                    .map(|x| x.as_str().to_string());

                let file_name = captures
                    .name("file_name_1")
                    .or(captures.name("file_name_2"))
                    .map(|x| x.as_str().to_string());

                let file_line = match captures
                    .name("file_line_1")
                    .or(captures.name("file_line_2"))
                    .map(|x| x.as_str())
                {
                    Some(x) => Some(x.parse()?),
                    None => None,
                };

                let function_offset = match captures.name("function_offset_1").map(|x| x.as_str()) {
                    Some(x) => Some(x.parse()?),
                    None => None,
                };

                let module_path = captures
                    .name("module_path_1")
                    .or(captures.name("module_path_2"))
                    .map(|x| x.as_str().to_string());

                let module_offset = match captures
                    .name("module_offset_1")
                    .or(captures.name("module_offset_2"))
                    .map(|x| x.as_str())
                {
                    Some(x) => Some(u64::from_str_radix(x, 16)?),
                    None => None,
                };

                let entry = StackEntry {
                    line,
                    address,
                    function_name,
                    file_name,
                    file_line,
                    module_path,
                    module_offset,
                    function_offset,
                };
                stack.push(entry);
            }
        }
    }

    Ok(stack)
}

pub(crate) fn parse_scariness(text: &str) -> Option<(u32, String)> {
    let pattern = r"(?m)^SCARINESS: (\d+) \(([^\)]+)\)\r?$";
    let re = Regex::new(pattern).ok()?;
    let captures = re.captures(text)?;
    let index = u32::from_str_radix(captures.get(1)?.as_str(), 10).ok()?;
    let value = captures.get(2)?.as_str().trim();

    Some((index, value.into()))
}

pub(crate) fn parse_asan_runtime_error(text: &str) -> Option<(String, String, String)> {
    let pattern = r"==\d+==((\w+) (CHECK failed): [^ \n]+)";
    let re = Regex::new(pattern).ok()?;
    let captures = re.captures(text)?;
    let summary = captures.get(1)?.as_str().trim();
    let sanitizer = captures.get(2)?.as_str().trim();
    let fault_type = captures.get(3)?.as_str().trim();
    Some((summary.into(), sanitizer.into(), fault_type.into()))
}

pub(crate) fn parse_asan_abort_error(text: &str) -> Option<(String, String, String)> {
    let pattern = r"==\d+==\s*(ERROR|WARNING): (?P<summary>(?P<sanitizer>\w+Sanitizer|libFuzzer): (?P<fault_type>ABRT|access-violation|deadly signal|use-of-uninitialized-value|stack-overflow)[^\n]*)";
    let re = Regex::new(pattern).ok()?;
    let captures = re.captures(text)?;
    let summary = captures.name("summary")?.as_str().trim();
    let sanitizer = captures.name("sanitizer")?.as_str().trim();
    let fault_type = captures.name("fault_type")?.as_str().trim();
    Some((summary.into(), sanitizer.into(), fault_type.into()))
}

pub(crate) fn parse_summary_base(text: &str) -> Option<(String, String, String)> {
    let pattern = r"SUMMARY: ((\w+): (data race|deadly signal|odr-violation|[^ \n]+).*)";
    let re = Regex::new(pattern).ok()?;
    let captures = re.captures(text)?;
    let summary = captures.get(1)?.as_str().trim();
    let sanitizer = captures.get(2)?.as_str().trim();
    let fault_type = captures.get(3)?.as_str().trim();
    Some((summary.into(), sanitizer.into(), fault_type.into()))
}

pub(crate) fn parse_summary(text: &str) -> Result<(String, String, String)> {
    if let Some((summary, sanitizer, fault_type)) = parse_summary_base(&text) {
        return Ok((summary, sanitizer, fault_type));
    }
    if let Some((summary, sanitizer, fault_type)) = parse_asan_abort_error(&text) {
        return Ok((summary, sanitizer, fault_type));
    }
    if let Some((summary, sanitizer, fault_type)) = parse_asan_runtime_error(&text) {
        return Ok((summary, sanitizer, fault_type));
    }

    bail!("unable to parse crash log summary")
}

#[cfg(test)]
mod tests {
    use super::{parse_asan_call_stack, StackEntry};
    use anyhow::Result;

    #[test]
    fn test_asan_stack_line() -> Result<()> {
        let test_cases = vec![
            (
                r"#0 0x1  (/path/to/bin+0x2)",
                vec![StackEntry {
                    line: r"#0 0x1  (/path/to/bin+0x2)".to_string(),
                    address: Some(1),
                    module_path: Some("/path/to/bin".to_string()),
                    module_offset: Some(2),
                    ..Default::default()
                }],
            ),
            (
                r"#42 0xf  (C:\WINDOWS\SYSTEM32\ntdll.dll+0x18004cec0)",
                vec![StackEntry {
                    line: r"#42 0xf  (C:\WINDOWS\SYSTEM32\ntdll.dll+0x18004cec0)".to_string(),
                    address: Some(15),
                    module_path: Some(r"C:\WINDOWS\SYSTEM32\ntdll.dll".to_string()),
                    module_offset: Some(6442766016),
                    ..Default::default()
                }],
            ),
            (
                r"#10 0xee in module::func(fuzzer::Fuzzer*, char const*, unsigned long) (/path/to/bin+0x123)",
                vec![StackEntry {
                    line: r"#10 0xee in module::func(fuzzer::Fuzzer*, char const*, unsigned long) (/path/to/bin+0x123)".to_string(),
                    address: Some(238),
                    function_name: Some("module::func(fuzzer::Fuzzer*, char const*, unsigned long)".to_string()),
                    module_path: Some(r"/path/to/bin".to_string()),
                    module_offset: Some(291),
                    ..Default::default()
                }],
            ),
            (
                r"#8 0x123 in from_file /path/to/source.c:67:12",
                vec![StackEntry {
                    line: r"#8 0x123 in from_file /path/to/source.c:67:12".to_string(),
                    address: Some(291),
                    function_name: Some("from_file".to_string()),
                    function_offset: Some(12),
                    file_name: Some("/path/to/source.c".to_string()),
                    file_line: Some(67),
                    ..Default::default()
                }],
            ),
        ];

        for (data, expected) in test_cases {
            let got = parse_asan_call_stack(data)?;
            assert_eq!(got, expected);
        }

        Ok(())
    }
}
