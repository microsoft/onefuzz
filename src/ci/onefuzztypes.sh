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
black ./onefuzztypes ./extra --check
flake8 ./onefuzztypes ./extra
bandit -r ./onefuzztypes
isort --profile black ./onefuzztypes ./extra --check
mypy ./onefuzztypes ./extra --ignore-missing-imports
pytest -v tests

cp dist/*.* ../../artifacts/sdk

echo 'verify webhook docs are up-to-date'
python -m venv build-docs
. build-docs/bin/activate
pip install -e .
python extra/generate-docs.py -o "../../docs/"
git diff ../../docs/webhook_events.md
deactivate
rm -rf build-docs
