# Custom Analysis Tasks

OneFuzz supports the ability to create user-defined analysis tasks, enabling
custom triage of crashes.

## Example use case

Users can automatically record the output of
[!analyze](https://docs.microsoft.com/en-us/windows-hardware/drivers/debugger/using-the--analyze-extension)
for crash using a `generic_generator` task with analyzer_exe of `cdb`, and the
`analyzer_options` of

```json
[
    "-c", "!analyze;q", "-logo", "{output_dir}\\{input_file_name_no_ext}.report",
     "{target_exe}", "{target_options}"
]
```

For a crash named `mycrash.txt`, this will create `mycrash.report` in the
`analysis` container.

This can be seen in the [radamsa](../src/cli/onefuzz/templates/radamsa.py)
template for any Windows targets.

See also:

* [Command Replacements](command-replacements.md)
* [Example to collect LLVM Source-Based Coverage using custom analysis](../src/cli/examples/llvm-source-coverage/README.md)
