#!/usr/bin/env bash
set -ex -o pipefail

# Required environment variables:
# - GOODBAD_DOTNET
# - LIBFUZZER_DOTNET
# - LIBFUZZER_DOTNET_LOADER
# - SHARPFUZZ

export GOODBAD_DLL='GoodBad/GoodBad.dll'

TMP=$(mktemp -d)
trap "rm -rf $TMP" EXIT

cd $TMP

cp -r ${GOODBAD_DOTNET} GoodBad

# Instrument DLL under test.
${SHARPFUZZ} GoodBad/GoodBad.dll

# Create seed and crash inputs.
printf 'good' > good.txt
printf 'bad!' > bad.txt

# Test individual env vars.
export LIBFUZZER_DOTNET_TARGET_ASSEMBLY="${GOODBAD_DLL}"
export LIBFUZZER_DOTNET_TARGET_CLASS='GoodBad.Fuzzer'
export LIBFUZZER_DOTNET_TARGET_METHOD='TestInput'

${LIBFUZZER_DOTNET} --target_path=${LIBFUZZER_DOTNET_LOADER} good.txt

# Expect nonzero exit.
! ${LIBFUZZER_DOTNET} --target_path=${LIBFUZZER_DOTNET_LOADER} bad.txt

# Test delimited env var.
export LIBFUZZER_DOTNET_TARGET="${LIBFUZZER_DOTNET_TARGET_ASSEMBLY}:${LIBFUZZER_DOTNET_TARGET_CLASS}:${LIBFUZZER_DOTNET_TARGET_METHOD}"
unset LIBFUZZER_DOTNET_TARGET_ASSEMBLY
unset LIBFUZZER_DOTNET_TARGET_CLASS
unset LIBFUZZER_DOTNET_TARGET_METHOD

${LIBFUZZER_DOTNET} --target_path=${LIBFUZZER_DOTNET_LOADER} good.txt

# Expect nonzero exit.
! ${LIBFUZZER_DOTNET} --target_path=${LIBFUZZER_DOTNET_LOADER} bad.txt
