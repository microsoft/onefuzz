# Understanding libFuzzer coverage within OneFuzz

The `libfuzzer_coverage` task in OneFuzz provides coverage data from
libFuzzer targets by extracting compiler-based coverage at runtime.

The extracted data isn't directly mappable to developer-consumable data at
this time. Microsoft uses this data to identify coverage growth and enables
reverse engineers to identify areas in the applications that need
investigation.

For developer-focused coverage, use [source-based coverage](https://clang.llvm.org/docs/SourceBasedCodeCoverage.html).

## Implementation Details

For each input in the corpus, the fuzzing target is run using a platform
specific debugging script which extracts a per-module `sancov` table. The
per-input `sancov` files are summaries for each module, as well as a total
for the target.

> NOTE: Per-module means the primary executable as well as any loaded .so or .dll that are instrumented with sancov.

* On Linux: [gdb script](../../src/agent/script/linux/libfuzzer-coverage/coverage_cmd.py).
    * Supported tables:
        * LLVM: `_sancov_cntrs`
* On Windows: [cdb script](../../src/agent/script/win64/libfuzzer-coverage/DumpCounters.js)
    * Supported tables:
        * LLVM: `_sancov_cntrs`
        * MSVC: `sancov$BoolFlag`, `sancov$8bitCounters`, `SancovBitmap`

## Understanding the coverage

Launching an [example libfuzzer](../../src/integration-tests/libfuzzer),
we'll see something like this:

```
$ onefuzz template libfuzzer basic bmc-2021-03-03 bmc 1 linux
INFO:onefuzz:creating libfuzzer from template
INFO:onefuzz:creating job (runtime: 24 hours)
INFO:onefuzz:created job: cd5660e3-3391-48d4-bfff-6f91533fc387
INFO:onefuzz:using container: oft-setup-3907b00953315a1693dcf057be11d03d
INFO:onefuzz:using container: oft-inputs-85f2d72678ad533c83e2be999481dec3
INFO:onefuzz:using container: oft-crashes-85f2d72678ad533c83e2be999481dec3
INFO:onefuzz:using container: oft-reports-85f2d72678ad533c83e2be999481dec3
INFO:onefuzz:using container: oft-unique-reports-85f2d72678ad533c83e2be999481dec3
INFO:onefuzz:using container: oft-unique-inputs-85f2d72678ad533c83e2be999481dec3
INFO:onefuzz:using container: oft-no-repro-85f2d72678ad533c83e2be999481dec3
INFO:onefuzz:using container: oft-coverage-3907b00953315a1693dcf057be11d03d
INFO:onefuzz:uploading target exe `fuzz.exe`
INFO:onefuzz:creating libfuzzer task
INFO:onefuzz:creating libfuzzer_coverage task
INFO:onefuzz:creating libfuzzer_crash_report task
INFO:onefuzz:done creating tasks
{
    "config": {
        "build": "1",
        "duration": 24,
        "name": "bmc",
        "project": "bmc-2021-03-03"
    },
    "end_time": "2021-03-04T21:44:43+00:00",
    "job_id": "cd5660e3-3391-48d4-bfff-6f91533fc387",
    "state": "init",
    "user_info": {
        "application_id": "db5c6d5c-f6d7-477c-9376-1889d3a6b183",
        "object_id": 77b19309-f8e0-4772-9756-f92ca3b35a0f",
        "upn": "example@contoso.com"
    }
}
$
```

After letting our task run for a while, we can fetch our coverage from the `oft-coverage` container listed above.

Let's examine the coverage generated thus far:
```
$ mkdir my-coverage
$ onefuzz containers files download_dir oft-coverage-3907b00953315a1693dcf057be11d03d ./my-coverage/
$ cd my-coverage; find . -type f 
./by-module/fuzz.exe.cov
./inputs/01ba4719c80b6fe911b091a7c05124b64eeece964e09c058ef8f9805daca546b.cov
./inputs/01ba4719c80b6fe911b091a7c05124b64eeece964e09c058ef8f9805daca546b/fuzz.exe.cov
./inputs/15dab3cc1c78958bc8c6d959cf708c2062e8327d3db873c2629b243c7e1a1759.cov
./inputs/15dab3cc1c78958bc8c6d959cf708c2062e8327d3db873c2629b243c7e1a1759/fuzz.exe.cov
./total.cov
$
```

What is shown here is:
* A per-module summary from all of the inputs.  This is stored as `by-module/module.cov`.
* A per-input/per-module sancov file.  This is stored as `inputs/SHA256_OF_INPUT/module.cov`.
* A per-input summary of all of the per-module sancov gathered for the input.  This is stored as `inputs/SHA256_OF_INPUT.cov`
* A summary of all of the coverage thus far, as `total.cov`

> NOTE: The `inputs/SHA256_OF_INPUT.cov` and `total.cov` are built by naively concatenating the per-module inputs.  The result is primarily useful for understanding coverage growth in general, but doesn't easily map back to source code.
