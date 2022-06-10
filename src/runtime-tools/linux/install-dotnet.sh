#!/bin/bash

apt update
apt install curl libicu-dev -y

mkdir /onefuzz/tools/dotnet

curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
chmod u+x dotnet-install.sh
. ./dotnet-install.sh --version 6.0.300 --install-dir /onefuzz/tools/dotnet

mkdir /etc/dotnet
touch /etc/dotnet/install_location
echo "/onefuzz/tools/dotnet" >> /etc/dotnet/install_location

dotnet tool install dotnet-dump --tool-path /onefuzz/tools
dotnet tool install dotnet-coverage --tool-path /onefuzz/tools
dotnet tool install dotnet-sos --tool-path /onefuzz/tools
