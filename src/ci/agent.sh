#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

set -ex

exists() {
    [ -e "$1" ]
}

export RUSTC_WRAPPER=$(which sccache)

mkdir -p artifacts/agent

cd src/agent
cargo fmt -- --check
# RUSTSEC-2019-0031: a dependency spin (pulled in from ring) is not actively maintained
# RUSTSEC-2020-0016: a dependency net2 (pulled in from tokio) is deprecated
# RUSTSEC-2020-0036: a dependency failure (pulled from proc-maps) is deprecated
cargo audit --deny warnings --deny unmaintained --deny unsound --deny yanked --ignore RUSTSEC-2019-0031 --ignore RUSTSEC-2020-0016 --ignore RUSTSEC-2020-0036 --ignore RUSTSEC-2020-0077
cargo-license -j > data/licenses.json
cargo build --release --locked
# export RUST_LOG=trace
export RUST_BACKTRACE=full
cargo test --release --manifest-path ./onefuzz-supervisor/Cargo.toml
# TODO: re-enable
# cargo test --release --manifest-path ./onefuzz-agent/Cargo.toml --features integration_test -- --nocapture
cargo test --release --manifest-path ./onefuzz/Cargo.toml

cp target/release/onefuzz-agent* ../../artifacts/agent
cp target/release/onefuzz-supervisor* ../../artifacts/agent
cp target/release/onefuzz-downloader* ../../artifacts/agent

if exists target/release/*.pdb; then
    for file in target/release/*.pdb; do
        cp ${file} ../../artifacts/agent
    done
fi
