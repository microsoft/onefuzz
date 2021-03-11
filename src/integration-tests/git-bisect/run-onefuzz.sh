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

echo "### run the local bisect tool, checking if we crash locally"
git bisect run ../src/bisect-onefuzz.sh test.txt

echo "### if the bisect works as expected, we should see 'commit 3'"
git show -s --format=%s

echo "### reseting git state"
git bisect reset