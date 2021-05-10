#!/bin/bash

set -e

# build our git repo with our samples in `test`
# (note, we don't care about the output of this script)
./build.sh 2>/dev/null > /dev/null

# create our crashing input
echo -n '3' > test/test.txt

cd test

# start the bisect, looking from HEAD backwards 8 commits
git bisect start HEAD HEAD~8 --
git bisect run ../src/bisect-local.sh test.txt
git bisect reset