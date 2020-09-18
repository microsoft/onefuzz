#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

mkdir -p artifacts/third-party/afl-linux

git clone https://github.com/google/AFL
cd AFL
# v2.75b
git checkout 82b5e359463238d790cadbe2dd494d6a4928bff3
make
(cd libdislocator; make)

cp -r afl-analyze afl-as afl-cmin afl-fuzz afl-gcc afl-gotcpu afl-plot afl-showmap afl-tmin afl-whatsup dictionaries libdislocator/libdislocator.so LICENSE ../artifacts/third-party/afl-linux
