# Install dotnet
&powershell -NoProfile -ExecutionPolicy unrestricted -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; &([scriptblock]::Create((Invoke-WebRequest -UseBasicParsing 'https://dot.net/v1/dotnet-install.ps1'))) -Version 6.0.300 -InstallDir c:/onefuzz/tools/dotnet"

# Set the variable for this current sessions
$env:DOTNET_ROOT = "c:/onefuzz/tools/dotnet"
# Set the variable for all future sessions
[Environment]::SetEnvironmentVariable("DOTNET_ROOT", "c:/onefuzz/tools/dotnet", "Machine")

# Install the necessary tools
cd c:/onefuzz/tools/dotnet
./dotnet.exe tool install dotnet-dump --tool-path c:/onefuzz/tools
./dotnet.exe tool install dotnet-coverage --tool-path c:/onefuzz/tools
./dotnet.exe tool install dotnet-sos --tool-path c:/onefuzz/tools

# Verify they're working
cd c:/onefuzz/tools
./dotnet-dump.exe -h
./dotnet-coverage.exe -h
./dotnet-sos.exe -h
