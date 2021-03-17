#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

set -ex

mkdir -p artifacts/third-party/aflpp-linux


sudo apt-get install -y llvm llvm-dev clang

git clone https://github.com/AFLplusplus/AFLplusplus
cd AFLplusplus
# checkout v3.11c
git checkout 23f7bee81c46ad4f0f65fa56d08064ab5f1e2e6f
make
(cd utils/libdislocator && make)
(cd utils/aflpp_driver && make); cp utils/aflpp_driver/*.so .

cp -rf afl-* *.so *.a *.o dictionaries LICENSE ../artifacts/third-party/aflpp-linux
