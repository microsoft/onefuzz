#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

set -ex

mkdir -p artifacts/third-party/aflpp-linux


sudo apt-get install -y llvm llvm-dev clang

git clone https://github.com/AFLplusplus/AFLplusplus
cd AFLplusplus
# checkout v3.12c
git checkout 2dac4e785fa9f27e8c59bb504cfa8942eba938be
make
(cd utils/libdislocator && make)
(cd utils/aflpp_driver && make); cp utils/aflpp_driver/*.so .

cp -rf afl-* *.so *.a *.o dictionaries LICENSE ../artifacts/third-party/aflpp-linux
