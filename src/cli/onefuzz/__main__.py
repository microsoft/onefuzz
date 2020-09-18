# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

"""
Command line interface to the Onefuzz service
"""

import sys

from onefuzz.__version__ import __version__
from onefuzz.api import Command, Endpoint, Onefuzz
from onefuzz.cli import execute_api


def main() -> int:
    return execute_api(Onefuzz(), [Endpoint, Command], __version__)


if __name__ == "__main__":
    sys.exit(main())
