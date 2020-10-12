#!/bin/bash 

BUILD_DIR=$(dirname $(realpath $0))
cd ${BUILD_DIR}

rm -rf tools
cp -r ../../runtime-tools ${BUILD_DIR}/tools

if [ ! -d ../../../artifacts/azcopy ]; then
    (cd ../../../; ./src/ci/agent.sh)
fi
(cd ../../../; cp artifacts/azcopy/azcopy ${BUILD_DIR}/tools/linux)

if [ -d ../../../artifacts/agent ]; then
    (cd ../../../; cp artifacts/agent/onefuzz-agent artifacts/agent/onefuzz-supervisor ${BUILD_DIR}/tools/linux)
else
    (cd ../../agent; cargo build --release; cp target/release/onefuzz-{agent,supervisor} ${BUILD_DIR}/tools/linux)
fi

docker build -t onefuzz:latest .
