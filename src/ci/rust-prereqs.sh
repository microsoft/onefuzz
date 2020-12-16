#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

set -ex

if ! sccache --help; then
    cargo install sccache
fi

sccache --start-server

export RUSTC_WRAPPER=$(which sccache)
cargo install cargo-audit

if ! cargo-license --help; then
    cargo install cargo-license
fi

rustup component add clippy
