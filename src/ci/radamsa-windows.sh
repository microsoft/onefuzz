#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

set -ex

mkdir -p artifacts/third-party/radamsa-win64

git clone https://gitlab.com/akihe/radamsa
cd radamsa
# latest checked version
git checkout 8121b78fb8f87e869cbeca931964df2b32435eb7
make
cp LICENCE bin/* ../artifacts/third-party/radamsa-win64
cp /c/msys64/usr/bin/msys-2.0.dll ../artifacts/third-party/radamsa-win64