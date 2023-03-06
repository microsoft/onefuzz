#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

set -ex

#export RUSTC_WRAPPER=$(which sccache)

mkdir -p artifacts/proxy

cd src/proxy-manager
cargo fmt -- --check
cargo clippy --release --all-targets -- -D warnings
cargo deny -L error check --allow vulnerability
cargo license -j > data/licenses.json
cargo build --release --locked
# export RUST_LOG=trace
export RUST_BACKTRACE=full
cargo test --release --locked

cp target/release/onefuzz-proxy-manager ../../artifacts/proxy
