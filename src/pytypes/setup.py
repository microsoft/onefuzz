#!/usr/bin/env python

# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import sys
from typing import Any, Dict, cast

import setuptools


def get_version() -> str:
    onefuzz: Dict[str, Any] = {}
    with open("onefuzztypes/__version__.py") as fh:
        # Exec our own __version__.py to pull out version string
        # without import
        exec(fh.read(), onefuzz)  # nosec
    version = onefuzz["__version__"]
    if "-v" in sys.argv:
        index = sys.argv.index("-v")
        sys.argv.pop(index)
        version += ".dev" + sys.argv.pop(index)
    return cast(str, version)


with open("requirements.txt") as f:
    requirements = f.read().splitlines()

    # remove any installer options (see pydantic example)
    requirements = [x.split(" ")[0] for x in requirements]

setuptools.setup(
    name="onefuzztypes",
    version=get_version(),
    description="Onefuzz Types Library",
    long_description=open("README.md").read(),
    long_description_content_type="text/markdown",
    url="https://github.com/microsoft/onefuzz/",
    author="Microsoft Corporation",
    author_email="fuzzing@microsoft.com",
    license_file="LICENSE",
    packages=setuptools.find_packages(),
    install_requires=requirements,
    zip_safe=False,
    include_package_data=True,
    package_data={"": ["*.md", "*.txt"], "onefuzztypes": ["py.typed"]},
)
