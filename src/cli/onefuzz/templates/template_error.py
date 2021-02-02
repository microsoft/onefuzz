#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.


class TemplateError(Exception):
    def __init__(self, message: str, status_code: int) -> None:
        super(TemplateError, self).__init__(message)
        self.message = message
        self.status_code = status_code
