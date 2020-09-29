#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

set -ex

mkdir -p artifacts/sdk

cd src/pytypes
pip install -r requirements-dev.txt
python setup.py sdist bdist_wheel

pip install -r requirements-lint.txt
black ./onefuzztypes --check
flake8 ./onefuzztypes
isort --profile black ./onefuzztypes --check
mypy ./onefuzztypes --ignore-missing-imports

cp dist/*.* ../../artifacts/sdk
