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

if [ ! -d ../../../artifacts/azcopy ]; then
   (cd ../../../; ./src/ci/agent.sh)
fi
(cd ../../../; cp artifacts/azcopy/azcopy ${BUILD_DIR}/tools/linux)

if [ -d ../../../artifacts/agent ]; then
   (cd ../../../; cp artifacts/agent/onefuzz-downloader ${BUILD_DIR}/tools/linux)
else
   (cd ../../agent; cargo build --release; cp target/release/onefuzz-downloader ${BUILD_DIR}/tools/linux)
fi

docker build -t onefuzz:${ONEFUZZ_VERSION} .