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
(cd utils/libdislocator && make)
(cd utils/aflpp_driver && make); cp examples/aflpp_driver/*.so .

cp -rf afl-* *.so *.a *.o dictionaries LICENSE ../artifacts/third-party/aflpp-linux
