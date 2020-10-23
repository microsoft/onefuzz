#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

# from typing import Dict, List, Optional
# from onefuzztypes.enums import OS, ContainerType, StatsFormat, TaskType
# from onefuzztypes.models import Job, NotificationConfig
# from onefuzztypes.primitives import Container, Directory, File

from onefuzz.api import Command
from onefuzztypes.models import OnefuzzTemplate

# from inspect import signature

from ._usertemplates import TEMPLATES
from ._render_template import build_input_config

# from . import JobHelper


class Prototype(Command):
    """ Pre-defined Prototype job """


def build_it(f: str, template: OnefuzzTemplate):
    config = build_input_config(template)
    print(f"building {f}")

    def func(self):  # , *args, **kwargs):
        print(f"I'm executing {f} - {config}")
        self.logger.warning(" %s", f)

    return func


for name, template in TEMPLATES.items():
    setattr(Prototype, name, build_it(name, template))