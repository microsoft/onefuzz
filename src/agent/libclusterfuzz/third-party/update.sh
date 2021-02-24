#!/bin/bash

set -ex

cd $(dirname "$(readlink -f "$0")")
if [ ! -f constants.py ]; then
curl -o constants.py https://raw.githubusercontent.com/google/clusterfuzz/master/src/python/lib/clusterfuzz/stacktraces/constants.py
fi
python build.py
rm -rf constants.py __pycache__ */__pycache__
(cd ../; cargo fmt)