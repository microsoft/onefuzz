# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

# Dockerized OneFuzz CLI

FROM ubuntu:20.04 AS installer-env

# Pull Request that contains OneFuzz release-artifacts
# used to create the Docker container
ARG PR
ARG GITHUB_TOKEN
ARG REPO="microsoft/onefuzz"

ENV GITHUB_ISSUE_TOKEN=${GITHUB_TOKEN}

RUN apt-get update && \
    apt-get install --yes --quiet curl \
        unzip \
        python3 \
        python3-pip \
        wget \
        && \
    pip3 install PyGithub && \
    mkdir onefuzz-prep

RUN wget https://aka.ms/downloadazcopy-v10-linux && \
    tar -xvf downloadazcopy-v10-linux


COPY "./utils/check-pr/github_client.py" "/onefuzz-prep"
RUN python3 /onefuzz-prep/github_client.py --destination /onefuzz-prep/ --pr ${PR} --repo ${REPO} && \
    unzip /onefuzz-prep/release-artifacts.zip -d /onefuzz-prep 


FROM ubuntu:20.04

COPY --from=installer-env ["/onefuzz-prep/sdk", "/onefuzz-sdk"]
COPY --from=installer-env ["/azcopy_linux_amd64_*/azcopy", "/usr/bin"]

RUN apt-get update && \
    apt-get install --yes --quiet \
        python3 \
        python3-pip \
        python-is-python3

RUN pip install /onefuzz-sdk/onefuzztypes-*.whl && \
    pip install /onefuzz-sdk/onefuzz-*.whl

CMD onefuzz --help && /bin/bash
