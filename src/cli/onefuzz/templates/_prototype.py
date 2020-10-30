#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

# from typing import Dict, List, Optional
# from onefuzztypes.enums import OS, ContainerType, StatsFormat, TaskType
# from onefuzztypes.models import Job, NotificationConfig
# from onefuzztypes.primitives import Container, Directory, File

from inspect import Parameter, signature
from typing import Any, List, Optional

from onefuzztypes.models import (
    OnefuzzTemplate,
    OnefuzzTemplateConfig,
    OnefuzzTemplateRequest,
)
from onefuzztypes.primitives import File

from onefuzz.api import Command

from ._render_template import build_input_config
from ._usertemplates import TEMPLATES

# from . import JobHelper


class Prototype(Command):
    """ Pre-defined Prototype job """

    # def _execute_

    def _parse_args(
        self, name: str, config: OnefuzzTemplateConfig, args: Any
    ) -> OnefuzzTemplateRequest:
        self.logger.warning("convert args into a Request here")
        for arg in args:
            pass

        request = OnefuzzTemplateRequest(
            template_name=name, user_fields={}, containers=[]
        )
        return request

    def _submit_request(self, request: OnefuzzTemplateRequest) -> int:
        self.logger.warning("do something with the request here...")
        return 3

    def _execute(
        self,
        name: str,
        config: OnefuzzTemplateConfig,
        args: Any,
    ) -> int:
        self.logger.warning("building: %s", name)
        self.logger.warning("config: %s", config)
        self.logger.warning("args: %s", args)

        request = self._parse_args(name, config, args)
        result = self._submit_request(request)
        return result


def config_to_params(config: OnefuzzTemplateConfig) -> List[Parameter]:
    params: List[Parameter] = []

    for entry in config.user_fields:
        is_optional = entry.default is None and entry.required is False
        if entry.name == "target_exe":
            annotation = Optional[File] if is_optional else File
        else:
            annotation = Optional[entry.type.value] if is_optional else entry.type.value

        default = entry.default if entry.default is not None else Parameter.empty
        param = Parameter(
            entry.name,
            Parameter.KEYWORD_ONLY,
            annotation=annotation,
            default=default,
        )
        params.append(param)

    return params


def build_template_func(name: str, template: OnefuzzTemplate) -> Any:
    config = build_input_config(template)

    def func(self: Prototype, **kwargs: Any) -> int:
        return self._execute(name, config, kwargs)

    sig = signature(func)
    params = [sig.parameters["self"]] + config_to_params(config)
    sig = sig.replace(parameters=tuple(params))
    func.__signature__ = sig  # type: ignore

    return func


for name, template in TEMPLATES.items():
    setattr(Prototype, name, build_template_func(name, template))
