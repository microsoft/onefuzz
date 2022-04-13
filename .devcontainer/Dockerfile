# See here for image contents: https://github.com/microsoft/vscode-dev-containers/tree/v0.224.3/containers/ubuntu/.devcontainer/base.Dockerfile

# focal = 20.04
ARG VARIANT="focal"
FROM mcr.microsoft.com/vscode/devcontainers/base:0-${VARIANT}

# Setup MS package signing key:
# NB: needs changing if VARIANT changes
RUN wget https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb && \
    dpkg -i packages-microsoft-prod.deb && \
    rm packages-microsoft-prod.deb

# Install APT HTTPS transport required for MS packages:
RUN apt-get update && export DEBIAN_FRONTEND=noninteractive && apt-get install -y apt-transport-https

# Install packages required for build:
RUN apt-get update && export DEBIAN_FRONTEND=noninteractive \
    && apt-get -y install --no-install-recommends \
    libunwind-dev clang build-essential libssl-dev pkg-config lldb \
    python3-minimal \
    dotnet-sdk-6.0 

### Setup Rust for 'vscode' user:
USER vscode

# Install Rust:
RUN curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh -s -- --default-toolchain 1.60 -y

ENV PATH="/home/vscode/.cargo/bin:${PATH}"

# Install Rust components:
RUN rustup component add rls rust-analysis rust-src rustfmt clippy

# Install required cargo commands:
RUN cargo install cargo-audit cargo-license sccache

USER root
### Done setting up Rust.
