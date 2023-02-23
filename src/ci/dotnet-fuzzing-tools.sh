#!/usr/bin/env bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.
set -ex -o pipefail

export SHARPFUZZ_REPO='https://github.com/Metalnem/sharpfuzz'
export SHARPFUZZ_COMMIT='66ef5c27df37ee26deb50140482dba20a393050e'

export LIBFUZZER_DOTNET_REPO='https://github.com/Metalnem/libfuzzer-dotnet'
export LIBFUZZER_DOTNET_COMMIT='4b4970168e8e105e1cda9ad67a2a0a3e81e34015'

# Script below assumes an absolute path.
export ARTIFACTS="${GITHUB_WORKSPACE}/artifacts/third-party/dotnet-fuzzing-linux"

# Create install destinations.
mkdir -p $ARTIFACTS
mkdir -p $ARTIFACTS/libfuzzer-dotnet
mkdir -p $ARTIFACTS/LibFuzzerDotnetLoader
mkdir -p $ARTIFACTS/sharpfuzz

# Install `libfuzzer-dotnet` pre-reqs.
sudo apt-get install -y llvm-12 llvm-12-dev clang-12

# Note that dotnet pre-reqs are presumed to be installed
# by the ci.yml setup (`setup-dotnet` action).

# Build SharpFuzz instrumentor.
git clone $SHARPFUZZ_REPO sharpfuzz
pushd sharpfuzz
git checkout $SHARPFUZZ_COMMIT
dotnet publish src/SharpFuzz.CommandLine -f net7.0 -c Release -o $ARTIFACTS/sharpfuzz --self-contained -r linux-x64
popd

# Build SharpFuzz and our dynamic loader harness for `libfuzzer-dotnet`.
pushd src/agent/LibFuzzerDotnetLoader
dotnet publish . -c Release -o $ARTIFACTS/LibFuzzerDotnetLoader --sc -r linux-x64
popd

# Build `libfuzzer-dotnet`.
git clone $LIBFUZZER_DOTNET_REPO
pushd libfuzzer-dotnet
git checkout $LIBFUZZER_DOTNET_COMMIT
clang -g -O2 -fsanitize=fuzzer libfuzzer-dotnet.cc -o libfuzzer-dotnet
cp libfuzzer-dotnet $ARTIFACTS/libfuzzer-dotnet
popd
