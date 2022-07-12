# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

$SHARPFUZZ_REPO = 'https://github.com/Metalnem/sharpfuzz'
$SHARPFUZZ_COMMIT = 'v2.0.0'

$LIBFUZZER_DOTNET_REPO = 'https://github.com/Metalnem/libfuzzer-dotnet'
$LIBFUZZER_DOTNET_COMMIT = '55d84f84b3540c864371e855c2a5ecb728865d97'

# Script below assumes an absolute path.
$ARTIFACTS = "${env:GITHUB_WORKSPACE}/artifacts/third-party/dotnet-fuzzing-windows"

# Create install destinations.
mkdir $ARTIFACTS
mkdir $ARTIFACTS/libfuzzer-dotnet
mkdir $ARTIFACTS/LibFuzzerDotnetLoader
mkdir $ARTIFACTS/sharpfuzz

# Build SharpFuzz instrumentor.
git clone $SHARPFUZZ_REPO sharpfuzz
pushd sharpfuzz
git checkout $SHARPFUZZ_COMMIT
dotnet publish src/SharpFuzz.CommandLine -f net6.0 -c Release -o $ARTIFACTS/sharpfuzz --sc -r win10-x64
popd

# Build SharpFuzz and our dynamic loader harness for `libfuzzer-dotnet`.
pushd src/agent/LibFuzzerDotnetLoader
dotnet publish . -c Release -o $ARTIFACTS/LibFuzzerDotnetLoader --sc -r win10-x64 -p:PublishSingleFile=true
popd

# Build `libfuzzer-dotnet`.
git clone $LIBFUZZER_DOTNET_REPO
pushd libfuzzer-dotnet
git checkout $LIBFUZZER_DOTNET_COMMIT
clang -g -O2 -fsanitize=fuzzer libfuzzer-dotnet-windows.cc -o libfuzzer-dotnet.exe
cp libfuzzer-dotnet.exe $ARTIFACTS/libfuzzer-dotnet
cp libfuzzer-dotnet.pdb $ARTIFACTS/libfuzzer-dotnet
popd
