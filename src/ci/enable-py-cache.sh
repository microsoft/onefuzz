#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

set -ex

SCRIPT_DIR=$(dirname ${BASH_SOURCE[0]})

sed -i "s/^##### from memoization import cached/from memoization import cached/" $(find . -name '*.py' -not -path .python_packages)
sed -i "s/^##### @cached/@cached/" $(find . -name '*.py' -not -path .python_packages)