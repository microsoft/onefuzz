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
# RUSTSEC-2022-0048: xml-rs is unmaintained
# RUSTSEC-2021-0139: ansi_term is unmaintained
cargo audit --deny warnings --deny unmaintained --deny unsound --deny yanked --ignore RUSTSEC-2022-0048 --ignore RUSTSEC-2021-0139
cargo-license -j > data/licenses.json
cargo build --release --locked
# export RUST_LOG=trace
export RUST_BACKTRACE=full
cargo test --release

cp target/release/onefuzz-proxy-manager ../../artifacts/proxy
