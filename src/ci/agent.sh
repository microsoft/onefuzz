#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

set -ex

exists() {
    [ -e "$1" ]
}

# only set RUSTC_WRAPPER if sccache exists
if sccache --help; then
    export RUSTC_WRAPPER=$(which sccache)
fi

# only set CARGO_INCREMENTAL on non-release builds
#
# This speeds up build time, but makes the resulting binaries slightly slower.
# https://doc.rust-lang.org/cargo/reference/profiles.html?highlight=incremental#incremental
if [ "${GITHUB_REF}" != "" ]; then
    TAG_VERSION=${GITHUB_REF#refs/tags/}
    if [ ${TAG_VERSION} == ${GITHUB_REF} ]; then
        export CARGO_INCREMENTAL=1
    fi
fi

mkdir -p artifacts/agent-$(uname)

cd src/agent

rustc --version
cargo --version
cargo audit --version
cargo clippy --version
cargo fmt --version

# unless we're doing incremental builds, start clean during CI
if [ X${CARGO_INCREMENTAL} == X ]; then
    cargo clean
fi

cargo fmt -- --check
# RUSTSEC-2020-0016: a dependency `net2` (pulled in from tokio) is deprecated
# RUSTSEC-2020-0036: a dependency `failure` (pulled from proc-maps) is deprecated
# RUSTSEC-2019-0036: a dependency `failure` (pulled from proc-maps) has type confusion vulnerability
# RUSTSEC-2021-0065: a dependency `anymap` is no longer maintained
# RUSTSEC-2020-0077: `memmap` dependency unmaintained, via `symbolic` (see: `getsentry/symbolic#304`)
# RUSTSEC-2020-0159: potential segfault in `time`, not yet patched (#1366)
# RUSTSEC-2020-0071: potential segfault in `chrono`, not yet patched (#1366)
cargo audit --deny warnings --deny unmaintained --deny unsound --deny yanked --ignore RUSTSEC-2020-0016 --ignore RUSTSEC-2020-0036 --ignore RUSTSEC-2019-0036 --ignore RUSTSEC-2021-0065 --ignore RUSTSEC-2020-0159 --ignore RUSTSEC-2020-0071 --ignore RUSTSEC-2020-0077
cargo-license -j > data/licenses.json
cargo build --release --locked
cargo clippy --release -- -D warnings
# export RUST_LOG=trace
export RUST_BACKTRACE=full
cargo test --release --workspace

# TODO: re-enable integration tests.
# cargo test --release --manifest-path ./onefuzz-task/Cargo.toml --features integration_test -- --nocapture

# TODO: once Salvo is integrated, this can get deleted
cargo build --release --manifest-path ./onefuzz-telemetry/Cargo.toml --all-features

cp target/release/onefuzz-task* ../../artifacts/agent-$(uname)
cp target/release/onefuzz-supervisor* ../../artifacts/agent-$(uname)
cp target/release/srcview* ../../artifacts/agent-$(uname)

if exists target/release/*.pdb; then
    for file in target/release/*.pdb; do
        cp ${file} ../../artifacts/agent-$(uname)
    done
fi
