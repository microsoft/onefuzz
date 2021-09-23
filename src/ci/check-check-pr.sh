#!/bin/bash

set -ex

cd src/utils/check-pr
pip install -r requirements.txt
pip install -r requirements-lint.txt
flake8 .
black --check .
isort --profile black --check .
mypy .
