#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

set -ex

mkdir -p artifacts/azcopy

wget --retry-connrefused -t 30 --waitretry=5 -O azcopy.zip https://aka.ms/downloadazcopy-v10-windows
unzip azcopy.zip
mv azcopy_windows*/* artifacts/azcopy/

wget --retry-connrefused -t 30 --waitretry=5 -O azcopy.tgz https://aka.ms/downloadazcopy-v10-linux
tar zxvf azcopy.tgz
mv azcopy_linux_amd64*/* artifacts/azcopy/
