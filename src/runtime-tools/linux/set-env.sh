# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.
export DOTNET_ROOT=/onefuzz/tools/dotnet
export DOTNET_CLI_HOME="$DOTNET_ROOT"
export LLVM_SYMBOLIZER_PATH=/onefuzz/bin/llvm-symbolizer
export RUST_LOG = "trace"