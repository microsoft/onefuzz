#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

set -ex

SCRIPT_DIR=$(dirname ${BASH_SOURCE[0]})
ONEFUZZ_VERSION=$(${SCRIPT_DIR}/get-version.sh)

cd src/docker/linux-node

rm -rf tools
mkdir -p tools/linux
DEST_DIR = $(realpath tools/linux)

if [ ! -d ../../../artifacts/azcopy ]; then
    (cd ../../../; ./src/ci/agent.sh)
fi
(cd ../../../; cp artifacts/azcopy/azcopy ${DEST_DIR})

if [ -d ../../../artifacts/agent ]; then
    (cd ../../../; cp artifacts/agent/onefuzz-downloader ${DEST_DIR})
else
    (cd ../../agent; cargo build --release; cp target/release/onefuzz-downloader ${DEST_DIR})
fi

docker build -t onefuzz:${ONEFUZZ_VERSION} .
