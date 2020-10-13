#!/bin/bash
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

set -x
mkdir -p /onefuzz/{bin,logs,tools,etc}
echo fuzz > /onefuzz/etc/mode
chmod -R a+rx /onefuzz/{bin,tools/linux}
/onefuzz/tools/linux/run.sh