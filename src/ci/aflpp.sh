#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

mkdir -p artifacts/third-party/aflpp-linux

git clone https://github.com/AFLplusplus/AFLplusplus
cd AFLplusplus
make
(test -e llvm_mode && cd llvm_mode && make)
(test -e gcc_plugin && cd gcc_plugin && make)
(cd examples/libdislocator && make)

cp -rf afl-* *.so *.a dictionaries examples/libdislocator/libdislocator.so LICENSE ../artifacts/third-party/aflpp-linux
