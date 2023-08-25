#!/bin/bash
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

set -ex

export PATH=$PATH:/onefuzz/bin:/onefuzz/tools/linux:/onefuzz/tools/linux/afl:/onefuzz/tools/linux/radamsa
export DOTNET_ROOT=/onefuzz/tools/dotnet
export ONEFUZZ_TOOLS=/onefuzz/tools
export ONEFUZZ_ROOT=/onefuzz
export RUST_BACKTRACE=full
export RUST_LOG="${RUST_LOG:=info}"
export LLVM_SYMBOLIZER_PATH=/onefuzz/bin/llvm-symbolizer

logger "onefuzz: starting up onefuzz"

#check if we are running in docker
if [ -f /.dockerenv ]; then
    echo "Running in docker: to optimize the experience make sure the host OS is setup properly, use the following commands:
    # 1) use core files, not external crash handler
    # 2) suffix core with PID: will be 'core.XXXX'
    # 3) disable ASLR
    # 4) set core dumping to default behavior
    sudo sysctl -w 'kernel.core_pattern=core' 'kernel.core_uses_pid=1' 'kernel.randomize_va_space=0' 'fs.suid_dumpable=1'

    # unlimit core files
    ulimit -c unlimited"
else
    # 1) use core files, not external crash handler
    # 2) suffix core with PID: will be 'core.XXXX'
    # 3) disable ASLR
    # 4) set core dumping to default behavior
    sudo sysctl -w 'kernel.core_pattern=core' 'kernel.core_uses_pid=1' 'kernel.randomize_va_space=0' 'fs.suid_dumpable=1'

    # unlimit core files
    ulimit -c unlimited
fi

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
        if [ -f /.dockerenv ]; then
            onefuzz-agent run --config /onefuzz/config.json "$@"
        else
            onefuzz-agent run --config /onefuzz/config.json --redirect-output /onefuzz/logs/
        fi
    ;;
    "repro")
        logger "onefuzz: starting repro"
        echo repro
        export ASAN_OPTIONS=abort_on_error=1
        repro.sh
    ;;
    *) logger "onefuzz: unknown command $MODE"; exit 1 ;;
esac
