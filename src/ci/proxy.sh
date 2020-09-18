#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

set -ex

mkdir -p artifacts/proxy

cd src/proxy-manager
cargo install sccache || echo 'already installed?'
export RUSTC_WRAPPER=$(which sccache)
cargo install cargo-audit
cargo install cargo-license || echo 'already installed'
cargo fmt -- --check
rustup component add clippy
cargo clippy --release -- -D warnings
# RUSTSEC-2020-0016: a dependency net2 (pulled in from tokio) is deprecated
cargo audit -D --ignore RUSTSEC-2020-0016
cargo-license -j > data/licenses.json
cargo build --release --locked
# export RUST_LOG=trace
export RUST_BACKTRACE=full
cargo test --release

cp target/release/onefuzz-proxy-manager ../../artifacts/proxy
