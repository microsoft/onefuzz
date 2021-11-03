#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

set -e

SCRIPT_DIR=$(dirname ${0})
ONEFUZZ_SRC_ROOT="$(realpath $SCRIPT_DIR/../..)/src"

function finish {
    cd $ONEFUZZ_SRC_ROOT/api-service/__app__
    $ONEFUZZ_SRC_ROOT/ci/enable-py-cache.sh

    cd $ONEFUZZ_SRC_ROOT/cli/onefuzz
    $ONEFUZZ_SRC_ROOT/ci/enable-py-cache.sh
}

trap finish EXIT

echo $ONEFUZZ_SRC_ROOT/api-service/
cd $ONEFUZZ_SRC_ROOT/api-service/
eval $(direnv export bash)
black ./__app__ ./tests
(cd __app__; $ONEFUZZ_SRC_ROOT/ci/disable-py-cache.sh)
mypy __app__ tests
(cd __app__; $ONEFUZZ_SRC_ROOT/ci/enable-py-cache.sh)
flake8 __app__ tests
isort --profile black __app__ tests

echo $ONEFUZZ_SRC_ROOT/cli
cd $ONEFUZZ_SRC_ROOT/cli
eval $(direnv export bash)
black ./onefuzz ./tests ./examples
isort --profile black onefuzz tests examples
(cd onefuzz; $ONEFUZZ_SRC_ROOT/ci/disable-py-cache.sh)
mypy onefuzz tests examples
(cd onefuzz; $ONEFUZZ_SRC_ROOT/ci/enable-py-cache.sh)
flake8 onefuzz tests examples

echo $ONEFUZZ_SRC_ROOT/pytypes
cd $ONEFUZZ_SRC_ROOT/pytypes
eval $(direnv export bash)
black ./onefuzztypes extra
isort --profile black onefuzztypes extra
# mypy onefuzztypes extra
mypy onefuzztypes
# flake8 onefuzztypes extra
flake8 onefuzztypes

echo $ONEFUZZ_SRC_ROOT/deployment
cd $ONEFUZZ_SRC_ROOT/deployment
eval $(direnv export bash)
black *.py
isort --profile black *.py
mypy *.py
flake8 *.py

cd $ONEFUZZ_SRC_ROOT/integration-tests
eval $(direnv export bash)
black *.py
isort --profile black *.py
mypy *.py
