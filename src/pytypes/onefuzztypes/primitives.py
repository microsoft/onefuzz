#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from enum import Enum
from typing import Any, Dict, NewType, Union, cast
from uuid import UUID

from onefuzztypes.validators import check_alnum, check_alnum_dash

Extension = Dict[str, Any]
Event = Union[str, int, UUID, Enum, Dict[str, str]]
Directory = NewType("Directory", str)
File = NewType("File", str)


# We ignore typing for the following super().__new__ calls
# specifically because mypy does not handle subclassing
# builtins well.  As is, mypy generates the error
# 'Too many arguments for "__new__" of "object"'
#
# However, this works in practice.


class Region(str):
    def __new__(cls, value: str) -> "Region":
        check_alnum(value)
        obj = super().__new__(cls, value)  # type: ignore
        return cast(Region, obj)


class Container(str):
    def __new__(cls, value: str) -> "Container":
        check_alnum_dash(value)
        obj = super().__new__(cls, value)  # type: ignore
        return cast(Container, obj)


class PoolName(str):
    def __new__(cls, value: str) -> "PoolName":
        check_alnum_dash(value)
        obj = super().__new__(cls, value)  # type: ignore
        return cast(PoolName, obj)
