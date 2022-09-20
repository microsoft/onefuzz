#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

set -ex

if ! sccache --help; then
    cargo install sccache
fi
# sccache --start-server
# export RUSTC_WRAPPER=$(which sccache)

cargo install cargo-audit cargo-llvm-cov

if ! cargo license --help; then
    cargo install cargo-license@0.4.2
fi
