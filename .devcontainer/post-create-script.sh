#!/bin/bash

# Install Azure Functions Core Tools 4
# Source: https://docs.microsoft.com/en-us/azure/azure-functions/functions-run-local?tabs=v4%2Clinux%2Ccsharp%2Cportal%2Cbash
echo "Installing Azure Functions Core Tools 4 ..."
cd /tmp
curl https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor > microsoft.gpg
sudo mv microsoft.gpg /etc/apt/trusted.gpg.d/microsoft.gpg
sudo sh -c 'echo "deb [arch=amd64] https://packages.microsoft.com/repos/microsoft-ubuntu-$(lsb_release -cs)-prod $(lsb_release -cs) main" > /etc/apt/sources.list.d/dotnetdev.list'
sudo apt-get update
sudo apt-get install azure-functions-core-tools-4

# Install azcopy
echo "Installing azcopy ..."
cd /tmp
wget https://aka.ms/downloadazcopy-v10-linux
tar -xvf downloadazcopy-v10-linux
sudo cp ./azcopy_linux_amd64_*/azcopy /usr/bin/

# Restore rust dependencies
echo "Restoring rust dependencies"
cd /workspaces/onefuzz/src/agent
cargo fetch

# Restore dotnet dependencies
echo "Restore dotnet dependencies"
cd /workspaces/onefuzz/src/ApiService
dotnet restore

sudo apt-get install direnv uuid-runtime
pip install wheel

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

echo "Install api-service"
cd /workspaces/onefuzz/src/api-service
echo "layout python3" >> .envrc
direnv allow
pip install -r requirements-dev.txt
cd __app__
pip install -r requirements.txt

cd /workspaces/onefuzz/src/utils
chmod u+x lint.sh
pip install types-six
