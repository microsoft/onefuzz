# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

FROM mcr.microsoft.com/oss/mirror/docker.io/library/ubuntu:20.04

RUN apt-get update && export DEBIAN_FRONTEND=noninteractive \
    && apt-get -y install --no-install-recommends \
    libunwind-dev clang build-essential libssl-dev pkg-config lldb

# creating a dummy sudo command
RUN echo "#!/bin/bash\n\$@" > /usr/bin/sudo && chmod +x /usr/bin/sudo
RUN mkdir /onefuzz
COPY . /onefuzz
RUN chmod +x /onefuzz/run.sh

RUN mkdir /onefuzz/etc && echo 'fuzz' > /onefuzz/etc/mode && mkdir /onefuzz/logs
WORKDIR /onefuzz


ENTRYPOINT ["./run.sh"]