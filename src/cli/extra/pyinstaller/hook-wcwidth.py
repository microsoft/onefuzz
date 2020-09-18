#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import os

from PyInstaller.utils.hooks import get_module_file_attribute

res_loc = os.path.dirname(get_module_file_attribute("wcwidth"))
datas = [
    (os.path.join(res_loc, "version.json"), "wcwidth"),
]
