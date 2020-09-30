#!/bin/bash
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.


APP_DIR=$(dirname $0)

if [ "$#" -ne 1 ]; then
    echo "usage: $0 <TARGET>"
    exit 1
fi

set -ex

TARGET=${1}
pushd ${APP_DIR}
VERSION=$(../ci/get-version.sh)
../ci/set-versions.sh

# clean up any previously built onefuzztypes packages
rm __app__/onefuzztypes*.whl

# build a local copy of onefuzztypes
rm -rf local-pytypes
cp -r ../pytypes local-pytypes
pushd local-pytypes
rm -f dist/*
python setup.py sdist bdist_wheel
cp dist/*.whl ../__app__
popd
rm -r local-pytypes

# deploy a the instance with the locally built onefuzztypes
pushd __app__
uuidgen > onefuzzlib/build.id
TYPELIB=$(ls onefuzztypes*.whl)
sed -i s,.*onefuzztypes.*,./${TYPELIB}, requirements.txt
func azure functionapp publish ${TARGET} --python
sed -i s,./onefuzztypes.*,onefuzztypes==0.0.0, requirements.txt
rm onefuzztypes*.whl
popd

../ci/unset-versions.sh
cat __app__/onefuzzlib/build.id