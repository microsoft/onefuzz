#!/usr/bin/env bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.
set -ex -o pipefail

export SHARPFUZZ_REPO='https://github.com/Metalnem/sharpfuzz'
export SHARPFUZZ_COMMIT='ec28c461b35fd917ae04cb1afc1028912f60a901'

export LIBFUZZER_DOTNET_REPO='https://github.com/Metalnem/libfuzzer-dotnet'
export LIBFUZZER_DOTNET_COMMIT='55d84f84b3540c864371e855c2a5ecb728865d97'

# Script below assumes an absolute path.
export ARTIFACTS="${GITHUB_WORKSPACE}/artifacts/third-party/dotnet-fuzzing-linux"

# Create install destinations.
mkdir -p $ARTIFACTS
mkdir -p $ARTIFACTS/libfuzzer-dotnet
mkdir -p $ARTIFACTS/LibFuzzerDotnetLoader
mkdir -p $ARTIFACTS/sharpfuzz

# Install `libfuzzer-dotnet` pre-reqs.
sudo apt-get install -y llvm-10 llvm-10-dev clang-10

# Install `SharpFuzz` and `LibFuzzerDotnetLoader` pre-reqs.
sudo apt-get install -y dotnet-sdk-6.0

# Prepare SharpFuzz dependency for sibling project reference, build instrumentor.
#
# Redundant when commit ec28c46 is released.
pushd src/agent
git clone $SHARPFUZZ_REPO
pushd sharpfuzz
git checkout $SHARPFUZZ_COMMIT
dotnet publish src/SharpFuzz.CommandLine -f net6.0 -c Release -o $ARTIFACTS/sharpfuzz --sc -r linux-x64
popd
popd

# Build SharpFuzz and our dynamic loader harness for `libfuzzer-dotnet`.
pushd src/agent/LibFuzzerDotnetLoader
dotnet publish . -c Release -o $ARTIFACTS/LibFuzzerDotnetLoader --sc -r linux-x64 -p:PublishSingleFile=true
popd

# Build `libfuzzer-dotnet`.
git clone $LIBFUZZER_DOTNET_REPO
pushd libfuzzer-dotnet
git checkout $LIBFUZZER_DOTNET_COMMIT
clang -g -O2 -fsanitize=fuzzer libfuzzer-dotnet.cc -o libfuzzer-dotnet
cp libfuzzer-dotnet $ARTIFACTS/libfuzzer-dotnet
popd
