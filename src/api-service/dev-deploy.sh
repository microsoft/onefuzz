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
cd ${APP_DIR}
(cd ../pytypes && python setup.py sdist bdist_wheel && cp dist/*.whl ../api-service/__app__)
cd __app__
uuidgen > onefuzzlib/build.id
sed -i s,onefuzztypes==0.0.0,./onefuzztypes-0.0.0-py3-none-any.whl, requirements.txt
func azure functionapp publish ${TARGET} --python
sed -i s,./onefuzztypes-0.0.0-py3-none-any.whl,onefuzztypes==0.0.0, requirements.txt
rm 'onefuzztypes-0.0.0-py3-none-any.whl'
cat onefuzzlib/build.id
