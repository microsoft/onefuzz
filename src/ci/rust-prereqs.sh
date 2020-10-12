#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

set -ex

cargo install sccache || echo 'already installed?'
export RUSTC_WRAPPER=$(which sccache)
cargo install cargo-audit
cargo install cargo-license || echo 'already installed?'
rustup component add clippy
