#!/bin/bash
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.
#

set -ex


cd $(dirname "$(readlink -f "$0")")
git clone --depth 1 https://github.com/google/clusterfuzz clusterfuzz-src
mv clusterfuzz-src/src/python/lib/clusterfuzz/stacktraces/constants.py .
mkdir -p ../data/stack-traces
cp clusterfuzz-src/src/python/tests/core/crash_analysis/stack_parsing/stack_analyzer_data/*.txt ../data/stack-traces/
python build.py
rm -rf constants.py __pycache__ */__pycache__ clusterfuzz-src
(cd ../; cargo fmt)
