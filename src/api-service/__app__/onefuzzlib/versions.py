#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import os
from typing import Dict

import semver
from memoization import cached
from onefuzztypes.responses import Version

from .__version__ import __version__


@cached
def read_local_file(filename: str) -> str:
    path = os.path.join(os.path.dirname(os.path.realpath(__file__)), filename)
    if os.path.exists(path):
        with open(path, "r") as handle:
            return handle.read().strip()
    else:
        return "UNKNOWN"


def versions() -> Dict[str, Version]:
    entry = Version(
        git=read_local_file("git.version"),
        build=read_local_file("build.id"),
        version=__version__,
    )
    return {"onefuzz": entry}


def is_minimum_version(*, version: str, minimum: str) -> bool:
    # >>> is_minimum_version(version="1.0.0", minimum="1.0.0")
    # True
    # >>> is_minimum_version(version="1.0.1", minimum="1.0.0")
    # True
    # >>> is_minimum_version(version="1.0.0", minimum="1.0.1")
    # False
    # >>>
    return bool(semver.compare(version, minimum) >= 0)
