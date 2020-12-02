#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from string import ascii_letters, digits
from typing import Optional

ALPHA_NUM = ascii_letters + digits
ALPHA_NUM_DASH = ALPHA_NUM + "-"
ALPHA_NUM_UNDERSCORE = ALPHA_NUM + "_"


def check_value(value: str, charset: str) -> str:
    if not all(x in charset for x in value):
        raise ValueError("invalid value: %s" % value)
    return value


def check_alnum(value: str) -> str:
    return check_value(value, ALPHA_NUM)


def check_alnum_dash(value: str) -> str:
    return check_value(value, ALPHA_NUM_DASH)


def check_alnum_underscore(value: str) -> str:
    return check_value(value, ALPHA_NUM_UNDERSCORE)


def check_template_name(value: str) -> str:
    if not value:
        raise ValueError("invalid value: %s" % value)

    if value[0] not in ALPHA_NUM:
        raise ValueError("invalid value: %s" % value)

    return check_alnum_underscore(value)


def check_template_name_optional(value: Optional[str]) -> Optional[str]:
    if value is None:
        return value

    return check_template_name(value)
