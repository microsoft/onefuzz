#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

set -ex

VERSION=$(cat CURRENT_VERSION)

SET_VERSIONS="src/pytypes/onefuzztypes/__version__.py src/api-service/__app__/onefuzzlib/__version__.py src/cli/onefuzz/__version__.py"
SET_REQS="src/cli/requirements.txt src/api-service/__app__/requirements.txt"

sed -i "s/0.0.0/${VERSION}/" ${SET_VERSIONS}
sed -i "s/onefuzztypes==0.0.0/onefuzztypes==${VERSION}/" ${SET_REQS}
