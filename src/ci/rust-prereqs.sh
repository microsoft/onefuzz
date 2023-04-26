#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

set -ex

cargo install sccache cargo-license@0.4.2 cargo-llvm-cov cargo-deny cargo-insta cargo-nextest

# sccache --start-server
# export RUSTC_WRAPPER=$(which sccache)
