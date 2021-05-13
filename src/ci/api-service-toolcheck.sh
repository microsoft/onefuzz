#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

set -ex

cd src/api-service

pip install -r requirements-dev.txt
# pip install -r requirements-lint.txt

black ./__app__ --check
flake8 ./__app__
bandit -r ./__app__
isort --profile black ./__app__ --check
mypy ./__app__ --ignore-missing-imports
pytest -v tests