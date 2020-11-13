#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from string import ascii_letters, digits


def check_alnum_dash(value: str) -> str:
    accepted = ascii_letters + digits + "-"
    if not all(x in accepted for x in value):
        raise ValueError("invalid value: %s" % value)
    return value


def check_alnum(value: str) -> str:
    accepted = ascii_letters + digits
    if not all(x in accepted for x in value):
        raise ValueError("invalid value: %s" % value)
    return value


def check_template_name(value: str) -> str:
    if not value:
        raise ValueError("invalid value: %s" % value)

    if value[0] not in ascii_letters + digits:
        raise ValueError("invalid value: %s" % value)

    return check_alnum_dash(value)
