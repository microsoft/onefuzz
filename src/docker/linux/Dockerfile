FROM ubuntu:18.04
LABEL maintainer="fuzzing@microsoft.com"
LABEL ABOUT="OneFuzz fuzzing container"

ARG DEBIAN_FRONTEND=noninteractive
RUN apt-get update \
	&& apt-get upgrade -y \
	&& apt-get install -y gdb gdbserver llvm-10 wget unzip libssl1.0.0 libunwind-dev \
	&& apt-get clean

RUN mkdir -p /onefuzz/bin /onefuzz/etc
ENV ASAN_SYMBOLIZER_PATH=/onefuzz/bin/llvm-symbolizer
RUN ln -f -s $(which llvm-symbolizer-10) $ASAN_SYMBOLIZER_PATH

ENV RUST_BACKTRACE=full
ENV RUST_LOG=debug
ENV PATH=/onefuzz/bin:/onefuzz/tools/linux:$PATH
COPY tools /onefuzz/tools

WORKDIR /onefuzz
ENTRYPOINT ["onefuzz-supervisor", "run"]