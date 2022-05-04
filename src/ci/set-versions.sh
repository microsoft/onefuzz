#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

set -ex

SCRIPT_DIR=$(dirname ${BASH_SOURCE[0]})
GET_VERSION=${SCRIPT_DIR}/get-version.sh
VERSION=${1:-$(${GET_VERSION})}
cd ${SCRIPT_DIR}/../../

SET_VERSIONS="src/pytypes/onefuzztypes/__version__.py src/api-service/__app__/onefuzzlib/__version__.py src/cli/onefuzz/__version__.py"
SET_REQS="src/cli/requirements.txt src/api-service/__app__/requirements.txt"

sed -i "s/0.0.0/${VERSION}/" ${SET_VERSIONS}
sed -i "s/onefuzztypes==0.0.0/onefuzztypes==${VERSION}/" ${SET_REQS}
