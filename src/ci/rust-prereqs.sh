#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

set -ex

cargo install --locked cargo-license@0.4.2 cargo-llvm-cov cargo-deny cargo-insta cargo-nextest
