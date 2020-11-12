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
pytest -v tests

cp dist/*.* ../../artifacts/sdk

echo 'verify webhook docs are up-to-date'
python -m venv build-docs
. build-docs/bin/activate
pip install -e .
python extra/generate-docs.py > ../../docs/webhook_events.md
git diff --quiet ../../docs/webhook_events.md
deactivate
rm -rf build-docs
