#!/bin/bash 

rm -rf tools
cp -r ../../runtime-tools tools

if [ ! -d ../../../artifacts/azcopy ]; then
    (cd ../../../; ./src/ci/agent.sh)
fi
(cd ../../../; cp artifacts/azcopy/azcopy src/docker/linux/tools/linux)

if [ -d ../../../artifacts/agent ]; then
    (cd ../../../; cp artifacts/agent/onefuzz-agent artifacts/agent/onefuzz-supervisor src/docker/linux/tools/linux)
else
    (cd ../../agent; cargo build --release; cp target/release/onefuzz-{agent,supervisor} ../src/docker/linux/tools/linux)
fi

docker build -t onefuzz:latest .
