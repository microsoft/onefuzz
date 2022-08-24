# See here for image contents: https://github.com/microsoft/vscode-dev-containers/tree/v0.224.3/containers/ubuntu/.devcontainer/base.Dockerfile

ARG VARIANT="ubuntu-22.04"

# note: keep this in sync with .github/workflows/ci.yml
ARG RUSTVERSION="1.63"

FROM mcr.microsoft.com/vscode/devcontainers/base:0-${VARIANT}

# Install packages required for build:
RUN apt-get update && export DEBIAN_FRONTEND=noninteractive \
    && apt-get -y install --no-install-recommends \
    libunwind-dev clang build-essential libssl-dev pkg-config lldb \
    bash-completion npm python-is-python3 \
    dotnet6

# Install Rust:
USER vscode
RUN curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh -s -- --default-toolchain ${RUSTVERSION} -y
USER root
