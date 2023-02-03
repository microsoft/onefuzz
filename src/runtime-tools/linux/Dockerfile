# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

FROM mcr.microsoft.com/oss/mirror/docker.io/library/ubuntu:20.04
# creating a dummy sudo command to allow the setup script to run
RUN echo "#!/bin/bash\n\$@" > /usr/bin/sudo && chmod +x /usr/bin/sudo
RUN mkdir /onefuzz
COPY . /onefuzz
RUN chmod +x /onefuzz/setup.sh
RUN export DEBIAN_FRONTEND=noninteractive && export DOCKER_BUILD=1 && cd /onefuzz && ./setup.sh fuzz
RUN export DEBIAN_FRONTEND=noninteractive \
    && apt-get -y install --no-install-recommends \
    libunwind-dev build-essential libssl-dev \
    pkg-config lldb ca-certificates
RUN mkdir -p /onefuzz/tools/linux \
    && mv /onefuzz/downloaded/* /onefuzz/tools/linux
RUN chmod +x /onefuzz/tools/linux/run.sh
RUN chmod +x /onefuzz/tools/linux/onefuzz-agent
RUN chmod +x /onefuzz/tools/linux/onefuzz-task
WORKDIR /onefuzz

ENTRYPOINT ["./tools/linux/run.sh"]