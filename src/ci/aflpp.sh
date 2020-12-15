#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

mkdir -p artifacts/third-party/aflpp-linux

sudo apt-get install -y llvm llvm-dev clang

git clone https://github.com/AFLplusplus/AFLplusplus
cd AFLplusplus
# checkout v3.00c
git checkout 8e712d1a740b30f9e2d5655d97d4cac6e8aed543
make
(test -d llvm_mode && cd llvm_mode && make)
#(test -d gcc_plugin && cd gcc_plugin && make)
(cd libdislocator && make)
(cd examples/aflpp_driver && make)

cp -rf afl-* *.so *.a *.o dictionaries LICENSE ../artifacts/third-party/aflpp-linux
