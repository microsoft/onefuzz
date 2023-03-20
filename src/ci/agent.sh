#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

set -ex

script_dir=$(dirname "$(realpath "${BASH_SOURCE[0]}")")

exists() {
    [ -e "$1" ]
}

SCCACHE=$(which sccache || echo '')
if [ -n "$SCCACHE" ]; then
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
rel_output_dir="artifacts/agent-$platform"
mkdir -p "$rel_output_dir"
output_dir=$(realpath "$rel_output_dir")

cd src/agent

rustc --version
cargo --version
cargo deny --version
cargo clippy --version
cargo fmt --version
cargo license --version

# unless we're doing incremental builds, start clean during CI
if [ X${CARGO_INCREMENTAL} == X ]; then
    cargo clean
fi

cargo fmt -- --check

cargo deny -L error check
cargo license -j > data/licenses.json
cargo build --release --locked
cargo clippy --release --locked --all-targets -- -D warnings
# export RUST_LOG=trace
export RUST_BACKTRACE=full

# Run tests and collect coverage 
# https://github.com/taiki-e/cargo-llvm-cov
cargo llvm-cov --locked --workspace --lcov --output-path "$output_dir/lcov.info"

# TODO: re-enable integration tests.
# cargo test --release --manifest-path ./onefuzz-task/Cargo.toml --features integration_test -- --nocapture

# TODO: once Salvo is integrated, this can get deleted
cargo build --release --locked --manifest-path ./onefuzz-telemetry/Cargo.toml --all-features

if [ -n "$SCCACHE" ]; then
    sccache --show-stats
fi

echo "Checking dependencies of binaries"

"$script_dir/check-dependencies.sh"

echo "Copying artifacts to $output_dir"

cp target/release/onefuzz-task* "$output_dir"
cp target/release/onefuzz-agent* "$output_dir"
cp target/release/srcview* "$output_dir"

if exists target/release/*.pdb; then
    for file in target/release/*.pdb; do
        cp "$file" "$output_dir"
    done
fi
