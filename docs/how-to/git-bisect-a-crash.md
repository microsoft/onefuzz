# Using OneFuzz with 'git bisect'

With a given crashing input, OneFuzz can be used in combination with [git bisect run](https://git-scm.com/docs/git-bisect) to find which commit introduced the bug in a git repo.

This example will let us identify the commit that introduced the bug that
`crash.txt` demonstrates.

## Write a test script

Using `git bisect run`, we need to provide a command to build and test your
target. For our example, we'll assume we have a `Makefile` that builds our
libfuzzer.

Example script `test.sh`:
```bash
#!/bin/bash
set -e

PROJECT=sample
TARGET=sample
BUILD=regression-$(git rev-parse HEAD)
POOL=linux

make clean
make 
onefuzz template regression libfuzzer ${PROJECT} ${TARGET} ${BUILD} ${POOL} --check_regression --delete_input_container --reports --crashes $*
```

> NOTES:
> * Specifying `--check_regression` will ensure the OneFuzz CLI exits non-zero upon finding a regression.
> * Specifying `--reports` without arguments tells OneFuzz "don't check any existing crash reports"
> * Specifying `--crashes $*` tells OneFuzz "check the filename passed on the command line for a crash"


We can test this script on the latest version of our target, we'll see it find a crash.
```
❯ test.sh crash.txt
clang -g3 -fsanitize=fuzzer -fsanitize=address fuzz.c -o fuzz.exe
INFO:onefuzz:creating regression task from template
INFO:onefuzz:creating job (runtime: 24 hours)
INFO:onefuzz:created job: 96cc4a7d-0ce1-4e6c-9c44-176ec9349bcf
INFO:onefuzz:using container: oft-setup-d84d8798c9af56959fb6c4cd77369594
INFO:onefuzz:using container: oft-crashes-6b6416fa416b50f3a91107d29b3aa0a7
INFO:onefuzz:using container: oft-reports-6b6416fa416b50f3a91107d29b3aa0a7
INFO:onefuzz:using container: oft-no-repro-6b6416fa416b50f3a91107d29b3aa0a7
INFO:onefuzz:using container: oft-unique-reports-6b6416fa416b50f3a91107d29b3aa0a7
INFO:onefuzz:using container: oft-regression-reports-1c011e5ef3c4532abefd940a3b5542cc
INFO:onefuzz:using container: oft-readonly-inputs-a2b807bab3c04b1d87e51ee1d45bce04
INFO:onefuzz:uploading target exe `fuzz.exe`
INFO:onefuzz:creating regression task
INFO:onefuzz:done creating tasks
- waiting on: libfuzzer_regression:init
/ waiting on: libfuzzer_regression:scheduled
| waiting on: libfuzzer_regression:setting_up
/ waiting on: libfuzzer_regression:running
INFO:onefuzz:tasks stopped
INFO:onefuzz:checking file: fd9407fb53266d6d97aae521313e35dc96c56c8e0915bdad3c75daa6bc4ed57a.json
ERROR:cli:command failed: regression identified: fd9407fb53266d6d97aae521313e35dc96c56c8e0915bdad3c75daa6bc4ed57a.json
❯ echo $?
1
❯
```

let's verify this doesn't crash on a known good build, in our case, the first commit:
```
❯ git checkout $(git log --format=%H |tail -n 1)
Note: switching to '4238a2ad85437bcead1f234e74bce7ae82919703'.

You are in 'detached HEAD' state. You can look around, make experimental
changes and commit them, and you can discard any commits you make in this
state without impacting any branches by switching back to a branch.

If you want to create a new branch to retain commits you create, you may
do so (now or later) by using -c with the switch command. Example:

  git switch -c <new-branch-name>

Or undo this operation with:

  git switch -

Turn off this advice by setting config variable advice.detachedHead to false

HEAD is now at 4238a2a commit 0
❯ test.sh crash.txt
clang -g3 -fsanitize=fuzzer -fsanitize=address fuzz.c -o fuzz.exe
INFO:onefuzz:creating regression task from template
INFO:onefuzz:creating job (runtime: 24 hours)
INFO:onefuzz:created job: 52271212-f7d9-43c9-98f4-f1a475deed75
INFO:onefuzz:using container: oft-setup-3abc79e8e0e058d781bcf851f304e212
INFO:onefuzz:using container: oft-crashes-25842c986b415a73b654d4d72bd003c2
INFO:onefuzz:using container: oft-reports-25842c986b415a73b654d4d72bd003c2
INFO:onefuzz:using container: oft-no-repro-25842c986b415a73b654d4d72bd003c2
INFO:onefuzz:using container: oft-unique-reports-25842c986b415a73b654d4d72bd003c2
INFO:onefuzz:using container: oft-regression-reports-e6d767191f4a50a499a01dff7c2cac13
INFO:onefuzz:using container: oft-readonly-inputs-13e00223d9874fa8ae1a94bb6d8e6104
INFO:onefuzz:uploading target exe `fuzz.exe`
INFO:onefuzz:creating regression task
INFO:onefuzz:done creating tasks
| waiting on: libfuzzer_regression:init
- waiting on: libfuzzer_regression:scheduled
| waiting on: libfuzzer_regression:setting_up
- waiting on: libfuzzer_regression:running
INFO:onefuzz:tasks stopped
INFO:onefuzz:checking file: 4e07408562bedb8b60ce05c1decfe3ad16b72230967de01f640b7e4729b49fce.json
INFO:onefuzz:no regressions
❯ echo $?
0
❯
```

Before we go on, we should put our repo back to the latest checkout of `main`.
```
❯ git checkout main
Previous HEAD position was 4238a2a commit 0
Switched to branch 'main'
❯ 
```

Now that we have a script that can identify if the bug of interest exists, we're ready for enabling `git bisect` to find when the bug was first introduced.

## Using our script with git

For this example, we'll check our HEAD branch through the previous 8 commits:
```
❯ git bisect start HEAD HEAD~8
Bisecting: 3 revisions left to test after this (roughly 2 steps)
[951d997542a79cf11e3e21b66370336cf0c56eda] commit 4
❯
```
Then we'll start our search:
```
❯ git bisect run ./test.sh ./crash.txt
running ./test.sh ./crash.txt
clang -g3 -fsanitize=fuzzer -fsanitize=address fuzz.c -o fuzz.exe
INFO:onefuzz:creating regression task from template
INFO:onefuzz:creating job (runtime: 24 hours)
INFO:onefuzz:created job: e9cfe710-f8b1-402f-8d1a-2071e0515d6e
INFO:onefuzz:using container: oft-setup-00707a41a8cb52f8aaceb83c7158a9dc
INFO:onefuzz:using container: oft-crashes-7bc81d050bf054d7bb471474afd77e85
INFO:onefuzz:using container: oft-reports-7bc81d050bf054d7bb471474afd77e85
INFO:onefuzz:using container: oft-no-repro-7bc81d050bf054d7bb471474afd77e85
INFO:onefuzz:using container: oft-unique-reports-7bc81d050bf054d7bb471474afd77e85
INFO:onefuzz:using container: oft-regression-reports-cd379169e0f658968f5e4a23910f1329
INFO:onefuzz:using container: oft-readonly-inputs-71cdb5e305a2474f96d93af3fd973fdd
INFO:onefuzz:uploading target exe `fuzz.exe`
INFO:onefuzz:creating regression task
INFO:onefuzz:done creating tasks
\ waiting on: libfuzzer_regression:init
| waiting on: libfuzzer_regression:scheduled
- waiting on: libfuzzer_regression:setting_up
| waiting on: libfuzzer_regression:running
INFO:onefuzz:tasks stopped
INFO:onefuzz:checking file: fd9407fb53266d6d97aae521313e35dc96c56c8e0915bdad3c75daa6bc4ed57a.json
ERROR:cli:command failed: regression identified: fd9407fb53266d6d97aae521313e35dc96c56c8e0915bdad3c75daa6bc4ed57a.json
Bisecting: 1 revision left to test after this (roughly 1 step)
[ac89e69eb134fc3dda4b7c0686f947423203f917] commit 2
running ./test.sh ./crash.txt
clang -g3 -fsanitize=fuzzer -fsanitize=address fuzz.c -o fuzz.exe
INFO:onefuzz:creating regression task from template
INFO:onefuzz:creating job (runtime: 24 hours)
INFO:onefuzz:created job: 15ed1cb0-16b2-4a74-a8a2-4cb53810f5cd
INFO:onefuzz:using container: oft-setup-54efc78261275c82aa50998441603324
INFO:onefuzz:using container: oft-crashes-d41be40dd1465103b42aae0b328f796e
INFO:onefuzz:using container: oft-reports-d41be40dd1465103b42aae0b328f796e
INFO:onefuzz:using container: oft-no-repro-d41be40dd1465103b42aae0b328f796e
INFO:onefuzz:using container: oft-unique-reports-d41be40dd1465103b42aae0b328f796e
INFO:onefuzz:using container: oft-regression-reports-0743b8121b925d64a171a94d8730854e
INFO:onefuzz:using container: oft-readonly-inputs-f86517f4fdbf46169a5434a60f4ffd35
INFO:onefuzz:uploading target exe `fuzz.exe`
INFO:onefuzz:creating regression task
INFO:onefuzz:done creating tasks
/ waiting on: libfuzzer_regression:init
- waiting on: libfuzzer_regression:scheduled
| waiting on: libfuzzer_regression:setting_up
- waiting on: libfuzzer_regression:running
INFO:onefuzz:tasks stopped
INFO:onefuzz:checking file: 4e07408562bedb8b60ce05c1decfe3ad16b72230967de01f640b7e4729b49fce.json
INFO:onefuzz:no regressions
Bisecting: 0 revisions left to test after this (roughly 0 steps)
[52b0249dcb5c07a614a2be47c3deccc3a19d29dd] commit 3
running ./test.sh ./crash.txt
clang -g3 -fsanitize=fuzzer -fsanitize=address fuzz.c -o fuzz.exe
INFO:onefuzz:creating regression task from template
INFO:onefuzz:creating job (runtime: 24 hours)
INFO:onefuzz:created job: 565cf7a4-e346-42b7-966d-775c41399f30
INFO:onefuzz:using container: oft-setup-1dddab2547df5ed4916e4d0ad77dc017
INFO:onefuzz:using container: oft-crashes-f653ff837eb556c2988f22f3253cfa33
INFO:onefuzz:using container: oft-reports-f653ff837eb556c2988f22f3253cfa33
INFO:onefuzz:using container: oft-no-repro-f653ff837eb556c2988f22f3253cfa33
INFO:onefuzz:using container: oft-unique-reports-f653ff837eb556c2988f22f3253cfa33
INFO:onefuzz:using container: oft-regression-reports-7f9bdfaa87145bb3a0348695c6b31503
INFO:onefuzz:using container: oft-readonly-inputs-1933ea274c6341e8a1b9c80ced9d4be4
INFO:onefuzz:uploading target exe `fuzz.exe`
INFO:onefuzz:creating regression task
INFO:onefuzz:done creating tasks
| waiting on: libfuzzer_regression:init
/ waiting on: libfuzzer_regression:waiting
- waiting on: libfuzzer_regression:scheduled
| waiting on: libfuzzer_regression:setting_up
- waiting on: libfuzzer_regression:running
INFO:onefuzz:tasks stopped
INFO:onefuzz:checking file: bea3e891086da4e359d8471707c2764ecb147dba73335cab426248e9a055bba9.json
ERROR:cli:command failed: regression identified: bea3e891086da4e359d8471707c2764ecb147dba73335cab426248e9a055bba9.json
52b0249dcb5c07a614a2be47c3deccc3a19d29dd is the first bad commit
commit 52b0249dcb5c07a614a2be47c3deccc3a19d29dd
Author: Example <example@contoso.com>
Date:   Thu Mar 18 15:21:16 2021 -0400

    commit 3

 fuzz.c | 1 +
 1 file changed, 1 insertion(+)
bisect run success
❯
```

With this result, we see that `52b0249dcb5c07a614a2be47c3deccc3a19d29dd` was
the commit that introduced this bug.

Now that you're done, use `git bisect reset` to put your git session back to normal.

## See Also
* [git-bisect](https://git-scm.com/docs/git-bisect) - Using binary search to find the commit that introduced a bug.
