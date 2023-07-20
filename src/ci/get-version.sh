#!/bin/bash
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

set -e

SCRIPT_DIR=$(dirname ${BASH_SOURCE[0]})
BASE_VERSION=$(cat ${SCRIPT_DIR}/../../CURRENT_VERSION)
BRANCH=$(git rev-parse --abbrev-ref HEAD)
GIT_HASH=$(git rev-parse HEAD)

# NB: ensure this code stays in sync the with version test in 
# .github/workflows/ci.yml

if [ "${GITHUB_REF}" != "" ]; then
    TAG_VERSION=${GITHUB_REF#refs/tags/}

    # this isn't a tag
    if [ ${TAG_VERSION} == ${GITHUB_REF} ]; then
        echo ${BASE_VERSION}+${GIT_HASH}
    else
        echo ${BASE_VERSION}
    fi
else
    if $(git diff --quiet); then
        echo ${BASE_VERSION}+${GIT_HASH}
    else
        echo ${BASE_VERSION}+${GIT_HASH}localchanges
    fi
fi
