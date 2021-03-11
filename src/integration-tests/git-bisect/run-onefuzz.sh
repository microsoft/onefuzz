#!/bin/bash

set -ex

# build our git repo with our samples in `test`
./build.sh

# create our crashing input
echo -n '3' > test/test.txt

cd test

# start the bisect, looking from HEAD backwards 8 commits
git bisect start HEAD HEAD~8 --

# run the local bisect tool, checking if we crash locally
git bisect run ../src/bisect-onefuzz.sh test.txt

# print the current commit (if this works, we expect to see 'commit 3')
echo "The next line should show 'commit 3'"
git show -s --format=%s

# reset git back the state it was prior to the bisect.
git bisect reset