# See here for image contents: https://github.com/microsoft/vscode-dev-containers/tree/v0.224.3/containers/ubuntu/.devcontainer/base.Dockerfile

ARG VARIANT="ubuntu-22.04"

FROM mcr.microsoft.com/devcontainers/base:${VARIANT}

# note: keep this in sync with .github/workflows/ci.yml
ARG RUSTVERSION="1.71.1"

# Install packages required for build:
RUN apt-get update && export DEBIAN_FRONTEND=noninteractive \
    && apt-get -y install --no-install-recommends \
    libunwind-dev clang build-essential libssl-dev pkg-config lldb \
    bash-completion npm \
    python-is-python3 direnv uuid-runtime python3-distutils python3-pip python3-venv \
    dotnet7 gh

# Install Rust:
USER vscode
RUN curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh -s -- --default-toolchain ${RUSTVERSION} -y

# Install other dependencies:
COPY install-dependencies.sh .
RUN ./install-dependencies.sh 

# Setup direnv for Python stuff:
RUN echo 'eval "$(direnv hook bash)"' >> ~/.bashrc

USER root
RUN rm install-dependencies.sh
