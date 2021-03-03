#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

set -ex

mkdir -p artifacts/third-party/aflpp-linux


sudo apt-get install -y llvm llvm-dev clang

git clone https://github.com/AFLplusplus/AFLplusplus
cd AFLplusplus
# checkout v3.10c
git checkout bd0a23de73011a390714b9f3836a46443054fdd5
make
(cd utils/libdislocator && make)
(cd utils/aflpp_driver && make); cp utils/aflpp_driver/*.so .

cp -rf afl-* *.so *.a *.o dictionaries LICENSE ../artifacts/third-party/aflpp-linux
