#!/bin/bash

# This script checks the OneFuzz agent binaries to ensure
# that we don't accidentally change their dependencies, and 
# create a binary that won't work on our standard Ubuntu images.
# 
# If we do make changes on purpose, the lists below should be updated.

# See issue and related links:
# https://github.com/microsoft/onefuzz/issues/1966

set -euo pipefail

script_dir=$(dirname "$(realpath "${BASH_SOURCE[0]}")")

function get-deps {
    ldd "$1" | awk '{ print $1 }' | sort -u
}

function check {
    wanted=$2
    got=$(get-deps "$1")
    if ! difference=$(diff -u --color <(echo "$wanted") <(echo "$got")); then 
        echo "unexpected dependencies for $1"
        echo "wanted:"
        echo "$wanted"
        echo "got:"
        echo "$got"
        echo "diff:"
        echo "$difference"
        echo "check and update dependency lists in src/ci/check-dependencies.sh, if needed"
        exit 1
    fi
}

check "$script_dir/../agent/target/release/onefuzz-task" \
"/lib64/ld-linux-x86-64.so.2
libc.so.6
libdl.so.2
liblzma.so.5
libm.so.6
libpthread.so.0
libunwind-ptrace.so.0
libunwind-x86_64.so.8
libunwind.so.8
linux-vdso.so.1"

check "$script_dir/../agent/target/release/onefuzz-agent" \
"/lib64/ld-linux-x86-64.so.2
libc.so.6
liblzma.so.5
libm.so.6
libunwind.so.8
linux-vdso.so.1"

check "$script_dir/../agent/target/release/srcview" \
"/lib64/ld-linux-x86-64.so.2
libc.so.6
libgcc_s.so.1
linux-vdso.so.1"
