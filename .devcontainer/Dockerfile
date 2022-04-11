# See here for image contents: https://github.com/microsoft/vscode-dev-containers/tree/v0.224.3/containers/ubuntu/.devcontainer/base.Dockerfile

# [Choice] Ubuntu version (use hirsuite or bionic on local arm64/Apple Silicon): hirsute, focal, bionic
ARG VARIANT="hirsute"
FROM mcr.microsoft.com/vscode/devcontainers/base:0-${VARIANT}

# Install basic packages required for build:
RUN apt-get update && export DEBIAN_FRONTEND=noninteractive \
    && apt-get -y install --no-install-recommends libunwind-dev clang build-essential libssl-dev pkg-config lldb python3-minimal

### Setup Rust for 'vscode' user:
USER vscode

# Install Rust:
RUN curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh -s -- --default-toolchain 1.60 -y

ENV PATH="/home/vscode/.cargo/bin:${PATH}"

# Install Rust components:
RUN rustup component add rls rust-analysis rust-src rustfmt clippy

# Install required cargo commands:
RUN cargo install cargo-audit

USER root
### Done setting up Rust.
