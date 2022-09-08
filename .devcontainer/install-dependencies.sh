#!/bin/bash

set -eux

# Note that this script runs as user 'vscode' during devcontainer setup.

# Rust global tools, needed to run CI scripts
"$HOME/.cargo/bin/cargo" install cargo-audit cargo-license@0.4.2

# NPM global tools
sudo npm install -g azurite azure-functions-core-tools@4

# Pip global tools
pip install wheel

# Other binaries
echo "Installing azcopy ..."
tmpdir=$(mktemp -d)
pushd "$tmpdir"
wget https://aka.ms/downloadazcopy-v10-linux -O - | tar -zxv
sudo cp ./azcopy_linux_amd64_*/azcopy /usr/bin/
popd
rm -rf "$tmpdir"
