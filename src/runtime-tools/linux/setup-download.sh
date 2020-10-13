#!/bin/bash
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

set -x

export ONEFUZZ_TOOLS=${ONEFUZZ_ROOT}/tools
export ASAN_SYMBOLIZER_PATH=${ONEFUZZ_ROOT}/bin/llvm-symbolizer

mkdir -p ${ONEFUZZ_ROOT}/{bin,logs,tools,etc}
chmod -R a+rx ${ONEFUZZ_ROOT}/{bin,tools/linux}

echo core | sudo tee /proc/sys/kernel/core_pattern || echo unable to set core pattern
echo 0 | sudo tee /proc/sys/kernel/randomize_va_space || echo unable to disable ASLR 
echo 1 | sudo tee /proc/sys/fs/suid_dumpable || echo unable to set suid_dumpable