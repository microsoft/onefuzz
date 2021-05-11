#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

set -ex

#export RUSTC_WRAPPER=$(which sccache)

mkdir -p artifacts/proxy

cd src/proxy-manager
cargo fmt -- --check
cargo clippy --release -- -D warnings
# RUSTSEC-2020-0016: a dependency net2 (pulled in from tokio) is deprecated
# RUSTSEC-2021-0065: a dependency anymap is no longer supported
cargo audit --deny warnings --deny unmaintained --deny unsound --deny yanked --ignore RUSTSEC-2020-0016 --ignore RUSTSEC-2021-0065
cargo-license -j > data/licenses.json
cargo build --release --locked
# export RUST_LOG=trace
export RUST_BACKTRACE=full
cargo test --release

cp target/release/onefuzz-proxy-manager ../../artifacts/proxy
