#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from PyInstaller.utils.hooks import collect_data_files

datas = collect_data_files("onefuzz")
