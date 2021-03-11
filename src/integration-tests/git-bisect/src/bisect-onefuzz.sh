#!/bin/bash

set -ex

PROJECT=${PROJECT:-regression-test}
TARGET=${TARGET:-$(uuidgen)}
BUILD=regression-$(git rev-parse HEAD)
POOL=${ONEFUZZ_POOL:-linux}

echo 'checking build'
git show -s --format=%s

make clean
make
onefuzz template regression libfuzzer ${PROJECT} ${TARGET} ${BUILD} ${POOL} --check_regressions --delete_input_container --reports --crashes $*
echo 'not this onefuzz job'