#!/bin/bash
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

set -ex

export PATH=$PATH:/onefuzz/bin:/onefuzz/tools/linux:/onefuzz/tools/linux/afl:/onefuzz/tools/linux/radamsa
export ONEFUZZ_TOOLS=/onefuzz/tools
export ONEFUZZ_ROOT=/onefuzz
export RUST_BACKTRACE=full
export RUST_LOG=info
export ASAN_SYMBOLIZER_PATH=/onefuzz/bin/llvm-symbolizer

logger "onefuzz: starting up onefuzz"

# use core files, not external crash handler
echo core | sudo tee /proc/sys/kernel/core_pattern
# disable ASLR
echo 0 | sudo tee /proc/sys/kernel/randomize_va_space
# set core dumping to default behavior
echo 1 | sudo tee /proc/sys/fs/suid_dumpable
RUNID=$(uuidgen)

cd /onefuzz
MODE=$(cat /onefuzz/etc/mode)
case ${MODE} in
    "proxy")
        logger "onefuzz: starting proxy"
        echo proxy
        onefuzz-proxy-manager --config /onefuzz/config.json
    ;;
    "fuzz")
        logger "onefuzz: starting fuzzing"
        echo fuzzing
        onefuzz-supervisor run --config /onefuzz/config.json > /onefuzz/logs/onefuzz-supervisor-${RUNID}.log 2> /onefuzz/logs/onefuzz-supervisor-${RUNID}.err
    ;;
    "repro")
        logger "onefuzz: starting repro"
        echo repro
        export ASAN_OPTIONS=abort_on_error=1
        repro.sh
    ;;
    *) logger "onefuzz: unknown command $1"; exit 1 ;;
esac
