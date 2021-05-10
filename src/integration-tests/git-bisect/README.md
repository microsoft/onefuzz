# git-bisect regression source

This assumes you have a working clang with libfuzzer, bash, and git.

This makes a git repo `test` with 9 commits.  Each commit after the first adds a bug.

* `commit 0` has no bugs.
* `commit 1` will additionally cause an abort if the input is `1`.
* `commit 2` will additionally cause an abort if the input is `2`.
* `commit 3` will additionally cause an abort if the input is `3`.
* etc.

This directory provides exemplar scripts that demonstrate how to perform
 `git bisect` with libfuzzer.

 * [run-local.sh](run-local.sh) builds & runs the libfuzzer target locally.  It uses [src/bisect-local.sh](src/bisect-local.sh) as the `git bisect run` command.
 * [run-onefuzz.sh](run-onefuzz.sh) builds the libfuzzer target locally, but uses OneFuzz to run the regression tasks.  It uses [src/bisect-onefuzz.sh](src/bisect-onefuzz.sh) as the `git bisect run` command.

With each project having their own unique paradigm for building, this model
allows plugging OneFuzz as a `bisect` command in whatever fashion your
project requires.