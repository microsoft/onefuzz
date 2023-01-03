# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

FROM mcr.microsoft.com/windows:ltsc2019 as base_onefuzz

SHELL ["powershell.exe", "-ExecutionPolicy", "RemoteSigned"]

RUN dir
RUN mkdir c:\onefuzz
RUN mkdir c:\temp
COPY tools.zip "c:\\temp"
RUN Expand-Archive -Path c:\temp\tools.zip -DestinationPath c:\temp\tools
RUN cp c:\temp\tools\win64\* c:\onefuzz
# RUN dir c:\onefuzz
RUN . c:\onefuzz\set-env.ps1

FROM base_onefuzz
COPY config.json c:\onefuzz

ENTRYPOINT onefuzz/run.sh