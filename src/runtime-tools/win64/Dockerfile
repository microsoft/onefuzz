# escape=`
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

ARG BASE_IMAGE=mcr.microsoft.com/windows:ltsc2019
FROM ${BASE_IMAGE}

SHELL ["powershell.exe", "-ExecutionPolicy", "Unrestricted", "-Command"]

RUN dir

COPY . c:\onefuzz\tools\win64\
COPY . c:\downloads\
RUN New-LocalUser -Name 'onefuzz' -Description 'onefuzz account' -NoPassword
RUN cd c:\downloads; & .\setup.ps1 -docker

WORKDIR "c:\onefuzz"

ENTRYPOINT & .\onefuzz-run.ps1 -docker