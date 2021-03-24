#!/bin/bash

set -ex

make clean
make
./fuzz.exe $*