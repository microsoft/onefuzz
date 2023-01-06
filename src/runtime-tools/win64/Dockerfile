# escape=`
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

FROM mcr.microsoft.com/windows:ltsc2019 as base_onefuzz

SHELL ["powershell.exe", "-ExecutionPolicy", "RemoteSigned", "-Command"]

RUN dir
RUN mkdir c:\onefuzz
COPY . c:\onefuzz

WORKDIR "c:\onefuzz"

ENTRYPOINT & .\onefuzz-run.ps1