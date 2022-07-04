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
export INSTALL_DIR="${GITHUB_WORKSPACE}/artifacts/third-party/dotnet-fuzzing-linux"

# Create install destination.
mkdir -p $INSTALL_DIR

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
mkdir $INSTALL_DIR/sharpfuzz
dotnet publish src/SharpFuzz.CommandLine -f net6.0 -c Release -o $INSTALL_DIR/sharpfuzz --sc -r linux-x64
popd
popd

# Build SharpFuzz and our dynamic loader harness for `libfuzzer-dotnet`.
pushd src/agent/LibFuzzerDotnetLoader
mkdir out
dotnet publish . -c Release -o out --sc -r linux-x64 -p:PublishSingleFile=true
cp out/LibFuzzerDotnetLoader $INSTALL_DIR
cp out/LibFuzzerDotnetLoader.pdb $INSTALL_DIR
popd

# Build `libfuzzer-dotnet`.
git clone $LIBFUZZER_DOTNET_REPO
pushd libfuzzer-dotnet
git checkout $LIBFUZZER_DOTNET_COMMIT
clang -g -O2 -fsanitize=fuzzer libfuzzer-dotnet.cc -o libfuzzer-dotnet
cp libfuzzer-dotnet $INSTALL_DIR
popd
