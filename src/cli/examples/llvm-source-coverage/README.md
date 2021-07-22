# Collecting Source Coverage using Analysis Tasks

The `generic_analysis` task can be used to perform a user-defined analysis of a target executable for every test input from some storage container.  

Running an application compiled with [LLVM's source-based code coverage](https://clang.llvm.org/docs/SourceBasedCodeCoverage.html) with each input can be used to generate source based coverage information.

This example demonstrates using `generic_analysis` and the LLVM source coverage tools to provide source-based coverage on every input for a job.  For more information, see [Custom Analysis Tasks](../../../../docs/custom-analysis.md)

* [source-coverage-libfuzzer.py](source-coverage-libfuzzer.py): A wrapper that will launch a standard `libfuzzer basic` job *with* a source-based coverage task.  (used below)
* [source-coverage.py](source-coverage.py): A wrapper that will launch a new job comprised of a source-based coverage task
* [setup](setup): a basic libFuzzer target that builds with and without source coverage enabled
* [tools/source-coverage.sh](tools/source-coverage.sh): a script that wraps `llvm-profdata` and `llvm-cov` to perform the source analysis

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
❯ # submit our basic job with an additional analysis task
❯ ./source-coverage-libfuzzer.py setup/ setup/fuzz.exe ./setup/fuzz-coverage.exe coverage-example 1 1 linux-1 ./tools/
INFO:onefuzz:creating libfuzzer from template
INFO:onefuzz:creating job (runtime: 24 hours)
INFO:onefuzz:created job: 61bc5c7c-d24f-4ebc-9bac-bec8fe040ade
INFO:onefuzz:using container: oft-setup-d1100b49a03c5a9483f140cee0676b87
INFO:onefuzz:using container: oft-inputs-6f3b76e7e841532bb7714375f564d483
INFO:onefuzz:using container: oft-crashes-6f3b76e7e841532bb7714375f564d483
INFO:onefuzz:using container: oft-reports-6f3b76e7e841532bb7714375f564d483
INFO:onefuzz:using container: oft-unique-reports-6f3b76e7e841532bb7714375f564d483
INFO:onefuzz:using container: oft-unique-inputs-6f3b76e7e841532bb7714375f564d483
INFO:onefuzz:using container: oft-no-repro-6f3b76e7e841532bb7714375f564d483
INFO:onefuzz:using container: oft-coverage-d1100b49a03c5a9483f140cee0676b87
INFO:onefuzz:using container: oft-regression-reports-06bdcba10b5f5e45bdb38ed924856426
INFO:onefuzz:uploading setup dir `setup/`
INFO:onefuzz:creating libfuzzer_regression task
INFO:onefuzz:creating libfuzzer task
INFO:onefuzz:creating coverage task
INFO:onefuzz:creating libfuzzer_crash_report task
INFO:onefuzz:done creating tasks
INFO:onefuzz:using container: oft-setup-d1100b49a03c5a9483f140cee0676b87
INFO:onefuzz:using container: oft-analysis-6f3b76e7e841532bb7714375f564d483
INFO:onefuzz:using container: oft-inputs-6f3b76e7e841532bb7714375f564d483
INFO:onefuzz:using container: oft-tools-6f3b76e7e841532bb7714375f564d483
INFO:onefuzz:Creating generic_analysis task
job:{
    "timestamp": null,
    "job_id": "61bc5c7c-d24f-4ebc-9bac-bec8fe040ade",
    "state": "init",
    "config": {
        "project": "coverage-example",
        "name": "1",
        "build": "1",
        "duration": 24
    },
    "error": null,
    "end_time": null,
    "task_info": null,
    "user_info": {
        "application_id": "00000000-0000-0000-0000-000000000000",
        "object_id": "00000000-0000-0000-0000-000000000000",
        "upn": "example@contoso.com"
    }
}
❯ # a little while later, check on the status of our job
❯ onefuzz
job: 61bc5c7c-d24f-4ebc-9bac-bec8fe040ade
project:coverage-example name:1 build:1

tasks:
50e1b076 target:fuzz-coverage.exe state:running type:generic_analysis
63880445 target:fuzz.exe state:stopped type:libfuzzer_regression
77fb6177 target:fuzz.exe state:running type:coverage
a8e3338c target:fuzz.exe state:running type:libfuzzer_crash_report
aae7ba1b target:fuzz.exe state:running type:libfuzzer_fuzz

containers:
setup           count:4     name:oft-setup-d1100b49a03c5a9483f140cee0676b87
analysis        count:14    name:oft-analysis-6f3b76e7e841532bb7714375f564d483
tools           count:1     name:oft-tools-6f3b76e7e841532bb7714375f564d483
crashes         count:11    name:oft-inputs-6f3b76e7e841532bb7714375f564d483
crashes         count:4     name:oft-crashes-6f3b76e7e841532bb7714375f564d483
unique_reports  count:3     name:oft-unique-reports-6f3b76e7e841532bb7714375f564d483
regression_reports count:0     name:oft-regression-reports-06bdcba10b5f5e45bdb38ed924856426
coverage        count:1     name:oft-coverage-d1100b49a03c5a9483f140cee0676b87
readonly_inputs count:11    name:oft-inputs-6f3b76e7e841532bb7714375f564d483
reports         count:4     name:oft-reports-6f3b76e7e841532bb7714375f564d483
no_repro        count:0     name:oft-no-repro-6f3b76e7e841532bb7714375f564d483
inputs          count:11    name:oft-inputs-6f3b76e7e841532bb7714375f564d483
❯ # lets check on the results of the analysis thus far
❯ onefuzz containers files list oft-analysis-6f3b76e7e841532bb7714375f564d483
{
    "files": [
        "coverage.lcov",
        "coverage.profdata",
        "coverage.report",
        "inputs/06a7e66b4ddb9d43b9007e20f351c8076a2f5c5c13ec6d683e1307eeee472f7a.profraw",
        "inputs/075de2b906dbd7066da008cab735bee896370154603579a50122f9b88545bd45.profraw",
        "inputs/0fc4f9bfb1e6850b77e130904c0d5f8d0bfabe9a658efee7c4c41ad0015bff22.profraw",
        "inputs/15dab3cc1c78958bc8c6d959cf708c2062e8327d3db873c2629b243c7e1a1759.profraw",
        "inputs/3ebe1b59762a1c8020c1efe3747dd07f0e30617ed60b4e6a5bee16b6ea421dd0.profraw",
        "inputs/594e519ae499312b29433b7dd8a97ff068defcba9755b6d5d00e84c524d67b06.profraw",
        "inputs/75558b9c2275acb05f57066ce1199be864c7affffece0b952edac02e785bbc9f.profraw",
        "inputs/bc9b8634ef85180578a9b501c901ce394ccd9087096fa4f298e4fc3752e60804.profraw",
        "inputs/c6b27b6743b120d83d5cc1d37b0f51acddcb69ff544763e7552efb7b575bac38.profraw",
        "inputs/c8bc644c4ddaaeafdb76142b72577e1f923b6797d87d254025f2fdf2b8225540.profraw",
        "inputs/e5e1b99e66064d2e9414a37158465eb4fdc1a8120b9fa8e10e9301b5fc25bc98.profraw"
    ]
}
❯ # this parses the report and checks that it's an coverage json report as we expect
❯ 1f containers files get oft-analysis-6f3b76e7e841532bb7714375f564d483 coverage.report | jq .type
"llvm.coverage.json.export"
❯ # now let's inspect the merged lcov file
❯ 1f containers files get oft-analysis-6f3b76e7e841532bb7714375f564d483 coverage.lcov |head -n 10
SF:/home/USERNAME/onefuzz/src/cli/examples/llvm-source-coverage/setup/simple.c
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
