# `coverage`

## Summary

The `coverage` crate is a library that provides components for recording binary code
coverage for userspace targets. The binary modules under test do not require static
instrumentation of any kind, but coverage will only be recorded for executable modules
that have debuginfo.

## Usage

### Example Tool

The `record` example demonstrates comprehensive usage of binary coverage recording and
conversion to source. It can be built from the `coverage` crate root via the command
`cargo build --examples --release.`

As an example, suppose you had a target name `app.exe`, with a directory of PNG test cases in `corpus`.

Binary coverage for a single specific input `example.png` could be recorded with the command:

```
record.exe -- ./app.exe corpus/example.png
```

The combined coverage for all inputs in the corpus can be recorded using the `--input-dir`/`-d` option:
```
record.exe -d corpus -- ./app.exe '@@'
```

In this case, the command after `--` is invoked multiple times. For each invocation, the
special `@@` input marker is replaced with the path to an input in `corpus`. The example
binary then merges the per-input coverage to produce the aggregated result.

To emit source + line coverage, just specify the `--output`/`-o` option:

```
record.exe -o source -d corpus -- ./app.exe '@@'
```

For Cobertura XML:

```
record.exe -o cobertura -d corpus -- ./app.exe '@@'
```

See `record.exe -h` for more options.

### Recording

The core type used for recording is `record::CoverageRecorder`. This accepts a Rust
standard library `Command`, and invokes it as a debuggee. Targets must exit before the
timeout, or no coverage will be returned. The output of recording is the `Recorded`
struct. This contains both an `output` field (target exit status and captured stdio
streams) and a `coverage` field. The `coverage` field contains the binary code coverage
organized by module and module-relative image offset.

### Allowlists

By default, coverage is recorded for all runtime-observed modules with debuginfo, and any
source file referred to by that debuginfo. Two allowlists can be used to control which
modules and source files have their coverage recorded.

Each allowlist is a flat text file with a simple syntax for path-matching rules.

- `/` or `\`-separated literal paths include an item (module or source file) exactly.
- `*` glob characters can be included anywhere, including within path components.
- Path patterns can be _excluded_ via the syntax `! <rule>`.
- Comments are supported using the `#` character.

If no allowlist is provided, the default allowlist contains only the rule `*`, which
includes all paths. If an allowlist is provided, then the default allow-all rule is
omitted. Files are then included only if they match an include rule but don't _also_ match
an (overriding) exclude rule.

An example source allowlist:

```
# 1. Record coverage for source files application root.
app/*

# 2. Also include library code, factored out of the application proper.
lib/*

# 3. But do _not_ record coverage for vendored library code.
! lib/vendor/*
```

With the above rules, we would have the following inclusion behavior:
- Include `src/main.c` (matches (1))
- Include `lib/utility.c` (matches (2))
- Exclude `lib/vendor/json.c` (matches (3), an exclude rule)
- Exclude `other/stuff.c` (does not match any allow rule)

### Source Coverage

#### Source File and Line

Source coverage is derived from binary coverage using debuginfo. The
`source::binary_to_source_coverage()` function converts a `BinaryCoverage` value to a
`SourceCoverage`, which describes the input binary coverage in terms of source files and
lines.

#### Cobertura XML

To obtain source and line coverage in the Cobertura XML format, you can directly convert a
`SourceCoverage` value to a `CoberturaCoverage` value using the `From` trait. The result
is serializable via the `CoberturaCoverage::to_string()` method. The conversion defined in
the `cobertura` module emits Cobertura designed to produce sensible HTML reports when
consumed by the ReportGenerator project.
