#!/bin/bash
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

set -ex

if [ $# -lt 3 ]; then
    echo usage: FUZZER RESULT_DIR FILE [... FILE_N]
    exit 1
fi

FUZZER=$1; shift
RESULTS=$1; shift

mkdir -p ${RESULTS}/inputs

MERGED=${RESULTS}/coverage.profdata
REPORT=${RESULTS}/coverage.report
LCOV=${RESULTS}/coverage.lcov

COV_TOOL=$(which llvm-cov || which llvm-cov-10)
PROF_TOOL=$(which llvm-profdata || which llvm-profdata-10)

for file in $@; do
    SHA=$(sha256sum ${file} | cut -d ' ' -f 1)
    RAW=${RESULTS}/inputs/${SHA}.profraw

    if [ -f ${RAW} ]; then
        continue
    fi
    
    LLVM_PROFILE_FILE=${RAW} ${FUZZER} ${file}
    if [ ! -f ${RAW} ]; then
        echo "no coverage file generated ${RAW}"
        exit 1
    fi

    if [ -f ${MERGED} ]; then
        ${PROF_TOOL} merge -output ${MERGED}.tmp ${RAW} ${MERGED}
        mv ${MERGED}.tmp ${MERGED}
    else 
        ${PROF_TOOL} merge -output ${MERGED} ${RAW}
    fi

    ${COV_TOOL} export ${FUZZER} -instr-profile=${MERGED} > ${REPORT}
    ${COV_TOOL} export ${FUZZER} -instr-profile=${MERGED} --format lcov > ${LCOV}
done
