#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from enum import Enum
from typing import Any, Dict, NewType, Union
from uuid import UUID

from onefuzztypes.validators import check_alnum, check_alnum_dash, check_len

Extension = Dict[str, Any]
Event = Union[str, int, UUID, Enum, Dict[str, str]]
Directory = NewType("Directory", str)
File = NewType("File", str)


class Region(str):
    def __new__(cls, value: str) -> "Region":
        check_alnum(value)
        obj = super().__new__(cls, value)
        return obj


class Container(str):
    def __new__(cls, value: str) -> "Container":
        check_alnum_dash(value)
        obj = super().__new__(cls, value)
        return obj


class PoolName(str):
    def __new__(cls, value: str) -> "PoolName":
        check_alnum_dash(value)
        obj = super().__new__(cls, value)
        return obj


class ContainerHoldName(str):
    def __new__(cls, value: str) -> "ContainerHoldName":
        check_alnum(value)
        # https://docs.microsoft.com/en-us/azure/storage/blobs/storage-blob-immutable-storage#legal-holds
        check_len(value, min_len=3, max_len=23)
        obj = super().__new__(cls, value)
        return obj
