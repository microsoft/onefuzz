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
    if [ "$(uname)" != 'Linux' ]; then
        wanted=$4 # Windows
    elif [ "$(uname -m)" != 'x86_64' ]; then
        wanted=$3 # ARM64
    fi
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
\
"/lib64/ld-linux-x86-64.so.2
libc.so.6
libdl.so.2
libgcc_s.so.1
liblzma.so.5
libm.so.6
libpthread.so.0
libstdc++.so.6
libunwind-ptrace.so.0
libunwind-x86_64.so.8
libunwind.so.8
linux-vdso.so.1" \
\
"/lib/ld-linux-aarch64.so.1
libc.so.6
libdl.so.2
libgcc_s.so.1
liblzma.so.5
libm.so.6
libpthread.so.0
libstdc++.so.6
libunwind-aarch64.so.8
libunwind-ptrace.so.0
linux-vdso.so.1" \
\
"ADVAPI32.dll
CRYPTBASE.DLL
GDI32.dll
KERNEL32.DLL
KERNELBASE.dll
MSASN1.dll
PSAPI.DLL
RPCRT4.dll
SSPICLI.DLL
apphelp.dll
bcrypt.dll
bcryptPrimitives.dll
crypt32.dll
dbghelp.dll
gdi32full.dll
msvcp_win.dll
msvcrt.dll
ntdll.dll
sechost.dll
secur32.dll
ucrtbase.dll
user32.dll
win32u.dll
ws2_32.dll"

check "$script_dir/../agent/target/release/onefuzz-agent" \
\
"/lib64/ld-linux-x86-64.so.2
libc.so.6
libdl.so.2
liblzma.so.5
libm.so.6
libpthread.so.0
libunwind.so.8
linux-vdso.so.1" \
\
"/lib/ld-linux-aarch64.so.1
libc.so.6
libdl.so.2
liblzma.so.5
libm.so.6
libpthread.so.0
linux-vdso.so.1" \
\
"ADVAPI32.dll
CRYPTBASE.DLL
KERNEL32.DLL
KERNELBASE.dll
MSASN1.dll
RPCRT4.dll
SSPICLI.DLL
apphelp.dll
bcrypt.dll
bcryptPrimitives.dll
crypt32.dll
dbghelp.dll
msvcrt.dll
ntdll.dll
sechost.dll
secur32.dll
ucrtbase.dll
ws2_32.dll"

check "$script_dir/../agent/target/release/srcview" \
"/lib64/ld-linux-x86-64.so.2
libc.so.6
libgcc_s.so.1
libpthread.so.0
linux-vdso.so.1" \
\
"/lib/ld-linux-aarch64.so.1
libc.so.6
libgcc_s.so.1
libpthread.so.0
linux-vdso.so.1" \
\
"CRYPTBASE.DLL
KERNEL32.DLL
KERNELBASE.dll
RPCRT4.dll
advapi32.dll
apphelp.dll
bcrypt.dll
bcryptPrimitives.dll
msvcrt.dll
ntdll.dll
sechost.dll"
