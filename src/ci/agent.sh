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

if [ "$platform" = 'Linux' ]; then
    # Run tests and collect coverage if on Linux
    # https://doc.rust-lang.org/stable/rustc/instrument-coverage.html#test-coverage
    RUSTFLAGS="-C instrument-coverage" cargo test --locked --workspace

    # merge all coverage files
    $(rustc --print sysroot)/lib/rustlib/x86_64-unknown-linux-gnu/bin/llvm-profdata merge -sparse **/default.profraw -o test.profdata
    # output coverage report (the ugly for loop is to find the right binaries; see link above)
    $(rustc --print sysroot)/lib/rustlib/x86_64-unknown-linux-gnu/bin/llvm-cov show --instr-profile=test.profdata \
        -Xdemangler=rustfilt --show-line-counts-or-regions --show-instantiations \
        --ignore-filename-regex='/\.cargo/(registry|git)' \
        $( for file in \
            $( \
                RUSTFLAGS="-C instrument-coverage" cargo test --locked --workspace --no-run --message-format=json \
                | jq -r "select(.profile.test == true) | .filenames[]"  \
                | grep -v dSYM - \
                ); \
            do printf "%s %s " -object $file; \
            done \
        ) > agent-coverage.txt
else 
    # Else just run tests
    cargo test --locked --workspace
fi


# TODO: re-enable integration tests.
# cargo test --release --manifest-path ./onefuzz-task/Cargo.toml --features integration_test -- --nocapture

# TODO: once Salvo is integrated, this can get deleted
cargo build --release --locked --manifest-path ./onefuzz-telemetry/Cargo.toml --all-features

if [ ! -z "$SCCACHE" ]; then
    sccache --show-stats
fi

cp target/release/onefuzz-task* ../../artifacts/agent-$(uname)
cp target/release/onefuzz-agent* ../../artifacts/agent-$(uname)
cp target/release/srcview* ../../artifacts/agent-$(uname)

if exists target/release/*.pdb; then
    for file in target/release/*.pdb; do
        cp ${file} ../../artifacts/agent-$(uname)
    done
fi
