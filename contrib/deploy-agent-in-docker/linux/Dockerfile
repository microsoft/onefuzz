# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

FROM mcr.microsoft.com/oss/mirror/docker.io/library/ubuntu:20.04

# Pull Request that contains OneFuzz release-artifacts
# used to create the Docker container
ARG RELEASE_VERSION

RUN apt-get update
RUN apt-get install --yes --quiet curl \
        unzip \
        wget

# creating a dummy sudo command
RUN echo "#!/bin/bash\n\$@" > /usr/bin/sudo && chmod +x /usr/bin/sudo

RUN wget https://github.com/microsoft/onefuzz/releases/download/${RELEASE_VERSION}/onefuzz-deployment-${RELEASE_VERSION}.zip && \
    unzip -j onefuzz-deployment-${RELEASE_VERSION}.zip tools/linux/* -d onefuzz-install/ && \
    cd onefuzz-install && \
    chmod +x ./onefuzz-agent && \
    chmod +x ./run.sh && \
    chmod +x ./setup.sh

RUN cd onefuzz-install && ./setup.sh

ENTRYPOINT onefuzz/run.sh