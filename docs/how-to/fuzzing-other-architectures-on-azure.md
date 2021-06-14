# Fuzzing non-X86_64 targets on Azure

Fuzzing non-x86_64 targets using libFuzzer on Azure makes use of two Open
Source capabilities:

2. [libFuzzer](https://www.llvm.org/docs/LibFuzzer.html), which performs the fuzzing
3. [qemu-user](https://qemu.readthedocs.io/en/latest/user/main.html), which provides user-space emulation.

TL/DR: check out our [libfuzzer-aarch64-crosscompile example](../../src/integration-tests/libfuzzer-aarch64-crosscompile/)

## Issues using qemu_user based LibFuzzer in OneFuzz

There are a few notable limitations when using `qemu-user` based libFuzzer targets in OneFuzz.
1. Live reproduction of crashes does not work.
2. The `libfuzzer-coverage` task does not work.
3. Only Linux is supported at this time.
4. Only `aarch64` CPU emulation has been tested.  PRs are welcome to support other architectures.
5. Custom setup scripts are not supported, though you can provide your own sysroot using `--sysroot`.

As such, a `libfuzzer qemu_user` template is available, which only uses the `libfuzzer_fuzz` and `libfuzzer_crash_report`.  As these issues are resolve, the template will be updated to include the additional tasks.

## Example

Let's build a simple `aarch64` target using GCC as a cross-compiler (See [our example](../../src/integration-tests/libfuzzer-aarch64-crosscompile/)).

1. Make sure you have QEMU and the appropriate cross compiler installed:
  ```bash
  sudo apt update
  sudo apt install -y qemu-user g++-aarch64-linux-gnu
  ```
2. Check out the libFuzzer libraries from `compiler-rt`.  Note, GCC requires the use of `pc-guard` for instrumentation, which was removed by the `compiler-rt` project.  As such, we need an older version of the library:
  ```
  git clone https://github.com/llvm-mirror/compiler-rt
  (cd compiler-rt; git checkout daa6759576548a2f3825faddaa6811cabbfb45eb)
  ```
3. Build the libFuzzer libraries *without* ASAN:
  ```
  mkdir -p fuzz-libs
  (cd fuzz-libs; aarch64-linux-gnu-g++ -c ../compiler-rt/lib/fuzzer/*.cpp)
  ```
4. Build our target:
  ```
  aarch64-linux-gnu-g++ -pthread -lasan -o fuzz.exe fuzz-libs/*.o fuzz.c -fsanitize=address -fsanitize-coverage=trace-pc
  ```
5. Verify our target built correctly:
  ```
  ASAN_OPTIONS=:detect_leaks=0 qemu-aarch64 -L /usr/aarch64-linux-gnu ./fuzz.exe -help=1
  ```
  > NOTE: `LSAN` does not work in `qemu-user`, so we need to disable that.

Now we're ready to deploy this target to OneFuzz. Note, if we have custom
libraries or want to run on a different version of linux, we'll need to
provide our own sysroot.

7. Now we can fuzz!
  Execute our `fuzz.exe` with `qemu-aarch64` our `inputs` directory:
  ```
  ASAN_OPTIONS=:detect_leaks=0 qemu-aarch64 -L /usr/aarch64-linux-gnu ./fuzz.exe ./inputs
  ```

  In a few seconds, you'll see output that looks something like this:
  ```
  INFO: Seed: 113138795
  INFO:        1 files found in ./inputs/                                                                                                                    INFO: -max_len is not provided; libFuzzer will not generate inputs larger than 4096 bytes
  INFO: seed corpus: files: 1 min: 3b max: 3b total: 3b rss: 314Mb
  #2      INITED cov: 5 ft: 5 corp: 1/3b lim: 4 exec/s: 0 rss: 317Mb
  #3      NEW    cov: 5 ft: 9 corp: 2/6b lim: 4 exec/s: 0 rss: 317Mb L: 3/3 MS: 1 ChangeBit-
  #4      NEW    cov: 5 ft: 13 corp: 3/9b lim: 4 exec/s: 0 rss: 317Mb L: 3/3 MS: 1 ChangeBit-
  #8      NEW    cov: 10 ft: 21 corp: 4/13b lim: 4 exec/s: 0 rss: 317Mb L: 4/4 MS: 4 ShuffleBytes-ChangeBit-ChangeBinInt-CrossOver-
  #9      NEW    cov: 10 ft: 26 corp: 5/17b lim: 4 exec/s: 0 rss: 317Mb L: 4/4 MS: 1 CopyPart-
  #10     NEW    cov: 10 ft: 31 corp: 6/21b lim: 4 exec/s: 0 rss: 317Mb L: 4/4 MS: 1 ChangeBit-
  #11     NEW    cov: 10 ft: 36 corp: 7/25b lim: 4 exec/s: 0 rss: 317Mb L: 4/4 MS: 1 ShuffleBytes-
  #12     NEW    cov: 10 ft: 37 corp: 8/28b lim: 4 exec/s: 0 rss: 317Mb L: 3/4 MS: 1 EraseBytes-
  #16     NEW    cov: 10 ft: 45 corp: 9/32b lim: 4 exec/s: 0 rss: 317Mb L: 4/4 MS: 4 CopyPart-CopyPart-CrossOver-ShuffleBytes-
  #18     NEW    cov: 11 ft: 46 corp: 10/36b lim: 4 exec/s: 0 rss: 317Mb L: 4/4 MS: 2 CopyPart-ChangeBit-
  #19     NEW    cov: 11 ft: 47 corp: 11/40b lim: 4 exec/s: 0 rss: 317Mb L: 4/4 MS: 1 ShuffleBytes-
  #20     REDUCE cov: 11 ft: 47 corp: 11/39b lim: 4 exec/s: 0 rss: 317Mb L: 2/4 MS: 1 EraseBytes-
  #30     NEW    cov: 11 ft: 52 corp: 12/43b lim: 4 exec/s: 0 rss: 317Mb L: 4/4 MS: 5 ChangeBit-CrossOver-CrossOver-CrossOver-CrossOver-
  #32     NEW    cov: 11 ft: 56 corp: 13/45b lim: 4 exec/s: 0 rss: 317Mb L: 2/4 MS: 2 CrossOver-ChangeBit-
  #38     REDUCE cov: 11 ft: 56 corp: 13/44b lim: 4 exec/s: 0 rss: 317Mb L: 1/4 MS: 1 EraseBytes-
  ```
## Launching our example in OneFuzz

These commands launches the a qemu-user based libFuzzer job in OneFuzz.  Note, we've added the arguments `--wait_for_running --wait_for_files inputs` such that we can monitor our job until we've seen at least one new input found via fuzzing.
```bash
TARGET_PROJECT=AARCH64
TARGET_NAME=Example
TARGET_BUILD=1
FUZZ_POOL=linux
onefuzz template libfuzzer qemu_user ${TARGET_PROJECT} ${TARGET_NAME} ${TARGET_BUILD} ${FUZZ_POOL} --wait_for_running --wait_for_files inputs
```

When we run this, we'll see output similar to:
```
WARNING:onefuzz:qemu_user jobs are a preview feature and may change in the future
INFO:onefuzz:creating job (runtime: 24 hours)
INFO:onefuzz:created job: fa5b7870-a51b-4f79-924f-2ef11a9830a0
INFO:onefuzz:using container: oft-setup-5346d5f33bc35c3d94cbc70f7815b85e
INFO:onefuzz:using container: oft-inputs-9c31136dc16a5aab8edf7666a614a285
INFO:onefuzz:using container: oft-crashes-9c31136dc16a5aab8edf7666a614a285
INFO:onefuzz:using container: oft-reports-9c31136dc16a5aab8edf7666a614a285
INFO:onefuzz:using container: oft-unique-reports-9c31136dc16a5aab8edf7666a614a285
INFO:onefuzz:using container: oft-no-repro-9c31136dc16a5aab8edf7666a614a285
INFO:onefuzz:uploading target exe `fuzz.exe`
INFO:onefuzz:uploading /tmp/tmp_9_f9kc3/setup.sh
INFO:onefuzz:uploading /tmp/tmp_9_f9kc3/fuzz.exe-wrapper.sh
INFO:onefuzz:creating libfuzzer_fuzz task
INFO:onefuzz:creating libfuzzer_crash_report task
INFO:onefuzz:done creating tasks
- waiting on: libfuzzer_crash_report:init, libfuzzer_fuzz:init
- waiting on: libfuzzer_crash_report:waiting, libfuzzer_fuzz:scheduled
| waiting on: libfuzzer_crash_report:waiting, libfuzzer_fuzz:setting_up
/ waiting on: libfuzzer_crash_report:waiting
| waiting on: libfuzzer_crash_report:scheduled
\ waiting on: libfuzzer_crash_report:setting_up
INFO:onefuzz:tasks started
\ waiting for new files: oft-inputs-9c31136dc16a5aab8edf7666a614a285
INFO:onefuzz:new files found
{
    "config": {
        "build": "1",
        "duration": 24,
        "name": "Example",
        "project": "AARCH64"
    },
    "end_time": "2021-02-27T16:41:24+00:00",
    "job_id": "fa5b7870-a51b-4f79-924f-2ef11a9830a0",
    "state": "enabled",
    "task_info": [
        {
            "state": "stopped",
            "task_id": "15cc52b9-b15b-4cf7-9fa7-669db67c8e0b",
            "type": "libfuzzer_fuzz"
        },
        {
            "state": "running",
            "task_id": "b3466396-7047-46f5-a58d-a21ada881e97",
            "type": "libfuzzer_crash_report"
        }
    ],
    "user_info": {
        "application_id": "e3b350d1-7863-4bd5-a4c0-83e6436c9c09",
        "object_id": "232a2ac6-f8fc-4eb3-b427-0c91bbab7eea",
        "upn": "example@contoso.com"
    }
}
```

## See Also
* [afl-unicorn](https://github.com/Battelle/afl-unicorn) - AFL-based application fuzzing using [Unicorn](https://www.unicorn-engine.org/)
* [TriforceAFL](https://github.com/nccgroup/TriforceAFL) - AFL-based full-system fuzzing using a fork of [QEMU](https://qemu.readthedocs.io/)
