#!/bin/bash 

set -ex

BUILD_DIR=$(dirname $(realpath $0))
cd ${BUILD_DIR}

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

docker build -t onefuzz:latest .