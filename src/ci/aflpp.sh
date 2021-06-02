#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

set -ex

mkdir -p artifacts/third-party/aflpp-linux


sudo apt-get install -y llvm llvm-dev clang

git clone https://github.com/AFLplusplus/AFLplusplus
cd AFLplusplus
# checkout v3.13c
git checkout 02294d368a29a0e748ab00c240d56c2c225b0941
make
(cd utils/libdislocator && make)

cp -rf afl-* *.so *.a *.o dictionaries LICENSE ../artifacts/third-party/aflpp-linux
