# Performing Source-Coverage using Analysis Tasks

The `generic_analysis` task can be used to perform user-defined analysis for every input.  

Running an application compiled with [LLVM's source-based code coverage](https://clang.llvm.org/docs/SourceBasedCodeCoverage.html) with each input can be used to generate source based coverage information.

This example demonstrates using `generic_analysis` and the LLVM source coverage tools to provide source-based coverage on every input for a job.

* [setup](setup): a basic libfuzzer target that builds with and without source coverage enabled
* [tools/source-coverage.sh](tools/source-coverage.sh): a script that wraps llvm-profdata and llvm-cov to perform the source analysis

This example generates the following data in the `analysis` container:
* inputs/`SHA256_OF_INPUT`.profraw: the "raw" coverage data for each input analyzed
* coverage.profdata: The merged coverage data using `llvm-profdata`
* coverage.report: The `JSON` report of the merged coverage data provided by `llvm-cov export`
* coverage.lcov : The `lcov` report of the merged coverage data provided by `llvm-cov export --format lcov`

```
❯ # build our libfuzzer
❯ cd setup/ 
❯ ls
Makefile  simple.c
❯ make
clang -g3 -fsanitize=fuzzer -fsanitize=address simple.c -o fuzz.exe
clang -g3 -fsanitize=fuzzer -fprofile-instr-generate -fcoverage-mapping simple.c -o fuzz-coverage.exe 
❯ cd ..
❯ # submit our basic job
❯ onefuzz template libfuzzer basic sample-coverage sample 1 linux --target_exe ./setup/fuzz.exe --setup_dir ./setup/
INFO:onefuzz:creating libfuzzer from template
INFO:onefuzz:creating job (runtime: 24 hours)
INFO:onefuzz:created job: d8b60d3c-0199-44f7-9604-6a17431d667d
INFO:onefuzz:using container: oft-setup-a0fc368219775dcda2d92aadaf3ba91e
INFO:onefuzz:using container: oft-inputs-aa8a8556b8005803a9c752f1f3bb0132
INFO:onefuzz:using container: oft-crashes-aa8a8556b8005803a9c752f1f3bb0132
INFO:onefuzz:using container: oft-reports-aa8a8556b8005803a9c752f1f3bb0132
INFO:onefuzz:using container: oft-unique-reports-aa8a8556b8005803a9c752f1f3bb0132
INFO:onefuzz:using container: oft-unique-inputs-aa8a8556b8005803a9c752f1f3bb0132
INFO:onefuzz:using container: oft-no-repro-aa8a8556b8005803a9c752f1f3bb0132
INFO:onefuzz:using container: oft-coverage-a0fc368219775dcda2d92aadaf3ba91e
INFO:onefuzz:using container: oft-regression-reports-a266651f3b0c5ed6a9767d1cf110179f
INFO:onefuzz:uploading setup dir `./setup/`
INFO:onefuzz:creating libfuzzer_regression task
INFO:onefuzz:creating libfuzzer task
INFO:onefuzz:creating coverage task
INFO:onefuzz:creating libfuzzer_crash_report task
INFO:onefuzz:done creating tasks
{
    "config": {
        "build": "1",
        "duration": 24,
        "name": "sample",
        "project": "sample-coverage"
    },                                                                                                                                                         "job_id": "d8b60d3c-0199-44f7-9604-6a17431d667d",
    "state": "init",
    "user_info": {
        "application_id": "00000000-0000-0000-0000-000000000000",
        "object_id": "00000000-0000-0000-0000-000000000000",
        "upn": "example@contoso.com"
    }
}
❯ # submit our analysis job
❯ python source-coverage.py ./setup/ ./setup/fuzz-coverage.exe sample-coverage sample 1 linux ./tools/
INFO:onefuzz:creating job (runtime: 24 hours)
INFO:onefuzz:created job: 89267599-b5e1-4acf-a04e-350f1da968c7
INFO:onefuzz:using container: oft-setup-a0fc368219775dcda2d92aadaf3ba91e
INFO:onefuzz:using container: oft-analysis-aa8a8556b8005803a9c752f1f3bb0132
INFO:onefuzz:using container: oft-inputs-aa8a8556b8005803a9c752f1f3bb0132
INFO:onefuzz:using container: oft-tools-aa8a8556b8005803a9c752f1f3bb0132
INFO:onefuzz:uploading setup dir `./setup/`
INFO:onefuzz:Creating generic_crash_report task
job:aa1d70aa-a562-4bde-be8d-dba982459352 task:d6f488a5-f95b-44a3-a149-cee7e951bfd9
❯ # a little while later, check on the status of our coverage task
❯ onefuzz status job aa1d
job: aa1d70aa-a562-4bde-be8d-dba982459352
project:sample-coverage name:sample build:1

tasks:
d6f488a5 target:fuzz-coverage.exe state:running type:generic_analysis

containers:
setup           count:4     name:oft-setup-a0fc368219775dcda2d92aadaf3ba91e
analysis        count:8     name:oft-analysis-aa8a8556b8005803a9c752f1f3bb0132
tools           count:1     name:oft-tools-aa8a8556b8005803a9c752f1f3bb0132
crashes         count:6     name:oft-inputs-aa8a8556b8005803a9c752f1f3bb0132
❯ # lets check on the results of the analysis thus far
❯ onefuzz containers files list oft-analysis-aa8a8556b8005803a9c752f1f3bb0132
{
    "files": [
        "coverage.lcov",
        "coverage.profdata",
        "coverage.report",
        "inputs/0e757fc99306b1e8460e487b00bd6903b9dab51b4e965856713fab88e96ade65.profraw",
        "inputs/15dab3cc1c78958bc8c6d959cf708c2062e8327d3db873c2629b243c7e1a1759.profraw",
        "inputs/27ecd0a598e76f8a2fd264d427df0a119903e8eae384e478902541756f089dd1.profraw",
        "inputs/34db310aad0a9797e717399db5f54c89e34070f380d9b89f0ae5be0c362231de.profraw",
        "inputs/50868f20258bbc9cce0da2719e8654c108733dd2f663b8737c574ec0ead93eb3.profraw",
        "inputs/66a0b53312c1d72c6bdc384d5a7e06a470c8a118c9599f59efe112a66cf85c37.profraw"
    ]
}
❯ # this parses the report and checks that it's an coverage json report as we expect
❯ 1f containers files get oft-analysis-aa8a8556b8005803a9c752f1f3bb0132 coverage.report | jq .type
"llvm.coverage.json.export"
❯ # now let's inspect the merged lcov file
❯ 1f containers files get oft-analysis-aa8a8556b8005803a9c752f1f3bb0132 coverage.lcov |head -n 10
SF:/home/bcaswell/projects/onefuzz/onefuzz/src/cli/examples/llvm-source-coverage/setup/simple.c
FN:8,LLVMFuzzerTestOneInput
FNDA:6,LLVMFuzzerTestOneInput
FNF:1
FNH:1
DA:8,6
DA:9,6
DA:10,6
DA:11,6
DA:12,1
❯  
```
