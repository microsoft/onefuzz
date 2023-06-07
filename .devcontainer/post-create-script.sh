#!/bin/bash

set -eux

# Restore rust dependencies
echo "Restoring rust dependencies"
cd /workspaces/onefuzz/src/agent
cargo fetch

# Restore dotnet dependencies
echo "Restore dotnet dependencies"
cd /workspaces/onefuzz/src/ApiService
dotnet restore

echo "Setting up venv"
cd /workspaces/onefuzz/src
python -m venv venv
. ./venv/bin/activate

echo "Installing pytypes"
cd /workspaces/onefuzz/src/pytypes
echo "layout python3" >> .envrc
direnv allow
pip install -e .

echo "Installing cli"
cd /workspaces/onefuzz/src/cli
echo "layout python3" >> .envrc
direnv allow
pip install -e .


cd /workspaces/onefuzz/src/utils
chmod u+x lint.sh
pip install types-six

cp /workspaces/onefuzz/.devcontainer/pre-commit /workspaces/onefuzz/.git/hooks
chmod u+x /workspaces/onefuzz/.git/hooks/pre-commit
