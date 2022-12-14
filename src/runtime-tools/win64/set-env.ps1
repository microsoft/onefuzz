# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

$env:Path += ";C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\;C:\onefuzz\win64;C:\onefuzz\tools\win64;C:\onefuzz\tools\win64\radamsa;$env:ProgramFiles\LLVM\bin"
$env:ONEFUZZ_ROOT = "C:\onefuzz"
$env:ONEFUZZ_TOOLS = "C:\onefuzz\tools"
$env:LLVM_SYMBOLIZER_PATH = "C:\Program Files\LLVM\bin\llvm-symbolizer.exe"
$env:RUST_LOG = "info"
# Set a session and machine scoped env var
$env:DOTNET_ROOT = "c:\onefuzz\tools\dotnet"
[Environment]::SetEnvironmentVariable("DOTNET_ROOT", $env:DOTNET_ROOT, "Machine")