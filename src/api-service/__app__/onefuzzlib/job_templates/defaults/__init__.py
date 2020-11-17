#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from .afl import afl_linux, afl_windows
from .libfuzzer import libfuzzer_linux, libfuzzer_windows

TEMPLATES = {
    "afl_windows": afl_windows,
    "afl_linux": afl_linux,
    "libfuzzer_linux": libfuzzer_linux,
    "libfuzzer_windows": libfuzzer_windows,
}
