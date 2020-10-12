#!/bin/bash 

cp -r ../../runtime-tools tools
(cd ../../../; ./src/ci/agent.sh; cp artifacts/agent/onefuzz-agent artifacts/agent/onefuzz-supervisor src/docker/linux/tools/linux)
(cd ../../../; ./src/ci/azcopy.sh; cp artifacts/azcopy/azcopy src/docker/linux/tools/linux)
docker build -t onefuzz:latest .