#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

set -ex

exists() {
    [ -e "$1" ]
}

SCCACHE=$(which sccache || echo '')
if [ ! -z "$SCCACHE" ]; then
    # only set RUSTC_WRAPPER if sccache exists
    export RUSTC_WRAPPER=$SCCACHE
    # incremental interferes with (disables) sccache
    export CARGO_INCREMENTAL=0
else
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
fi

platform=$(uname)
mkdir -p "artifacts/agent-$platform"

cd src/agent

rustc --version
cargo --version
cargo audit --version
cargo clippy --version
cargo fmt --version
cargo license --version

# unless we're doing incremental builds, start clean during CI
if [ X${CARGO_INCREMENTAL} == X ]; then
    cargo clean
fi

cargo fmt -- --check
# RUSTSEC-2022-0048: xml-rs is unmaintained
# RUSTSEC-2021-0139: ansi_term is unmaintained
cargo audit --deny warnings --deny unmaintained --deny unsound --deny yanked --ignore RUSTSEC-2022-0048 --ignore RUSTSEC-2021-0139
cargo license -j > data/licenses.json
cargo build --release --locked
cargo clippy --release --locked --all-targets -- -D warnings
# export RUST_LOG=trace
export RUST_BACKTRACE=full

# Run tests and collect coverage 
# https://github.com/taiki-e/cargo-llvm-cov
cargo llvm-cov --locked --workspace --lcov --output-path lcov.info

# TODO: re-enable integration tests.
# cargo test --release --manifest-path ./onefuzz-task/Cargo.toml --features integration_test -- --nocapture

# TODO: once Salvo is integrated, this can get deleted
cargo build --release --locked --manifest-path ./onefuzz-telemetry/Cargo.toml --all-features

if [ ! -z "$SCCACHE" ]; then
    sccache --show-stats
fi

cp target/release/onefuzz-task* "../../artifacts/agent-$platform"
cp target/release/onefuzz-agent* "../../artifacts/agent-$platform"
cp target/release/srcview* "../../artifacts/agent-$platform"

if exists target/release/*.pdb; then
    for file in target/release/*.pdb; do
        cp "$file" "../../artifacts/agent-$platform"
    done
fi
