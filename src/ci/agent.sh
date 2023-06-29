#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

set -ex

script_dir=$(dirname "$(realpath "${BASH_SOURCE[0]}")")

exists() {
    [ -e "$1" ]
}

platform=$(uname --kernel-name --machine)
platform=${platform// /-} # replace spaces with dashes
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

cargo fmt -- --check

cargo deny -L error check
cargo license -j > data/licenses.json
cargo build --release --locked
cargo clippy --release --locked --all-targets -- -D warnings
# export RUST_LOG=trace
export RUST_BACKTRACE=full

# Run tests and collect coverage 
# https://github.com/taiki-e/cargo-llvm-cov
cargo llvm-cov nextest --all-targets --locked --workspace --lcov --output-path "$output_dir/lcov.info"

# TODO: re-enable integration tests.
# cargo test --release --manifest-path ./onefuzz-task/Cargo.toml --features integration_test -- --nocapture

# TODO: once Salvo is integrated, this can get deleted
cargo build --release --locked --manifest-path ./onefuzz-telemetry/Cargo.toml --all-features

echo "Checking dependencies of binaries"

"$script_dir/check-dependencies.sh"

if [ "$(uname)" == 'Linux' ]; then
    echo "Stripping unneeded debug info"
    # see: https://github.com/benesch/materialize/blob/02cc34cb7c7d9f1b775fe95e1697b797d8fa9b6d/misc/python/mzbuild.py#L166-L183
    objcopy -R .debug_pubnames -R .debug_pubtypes target/release/onefuzz-task
    objcopy -R .debug_pubnames -R .debug_pubtypes target/release/onefuzz-agent
    objcopy -R .debug_pubnames -R .debug_pubtypes target/release/srcview

    echo "Compressing debug symbols"
    objcopy --compress-debug-sections target/release/onefuzz-task
    objcopy --compress-debug-sections target/release/onefuzz-agent
    objcopy --compress-debug-sections target/release/srcview
fi

echo "Copying artifacts to $output_dir"

cp target/release/onefuzz-task* "$output_dir"
cp target/release/onefuzz-agent* "$output_dir"
cp target/release/srcview* "$output_dir"

if exists target/release/*.pdb; then
    for file in target/release/*.pdb; do
        cp "$file" "$output_dir"
    done
fi
