#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

# from typing import Dict, List, Optional
# from onefuzztypes.enums import OS, ContainerType, StatsFormat, TaskType
# from onefuzztypes.models import Job, NotificationConfig
# from onefuzztypes.primitives import Container, Directory, File

from inspect import Parameter, signature
from typing import Any, Dict, List, Optional

from onefuzztypes.enums import OS, ContainerType
from onefuzztypes.models import (
    OnefuzzTemplate,
    OnefuzzTemplateConfig,
    OnefuzzTemplateRequest,
    TaskContainers,
)
from onefuzztypes.primitives import Directory, File

from onefuzz.api import Command

from . import _build_container_name
from ._render_template import build_input_config
from ._usertemplates import TEMPLATES

# from . import JobHelper


def container_type_name(container_type: ContainerType) -> str:
    return container_type.name + "_dir"


class Prototype(Command):
    """ Pre-defined Prototype job """

    def _parse_container_args(
        self, config: OnefuzzTemplateConfig, args: Any
    ) -> List[TaskContainers]:
        containers = []
        for container_type in config.containers:
            if container_type in args["container_names"]:
                container_name = args["container_names"][container_type]
            else:
                container_name = _build_container_name(
                    self.onefuzz,
                    container_type,
                    args["project"],
                    args["name"],
                    args["build"],
                    OS.linux,
                )
            containers.append(TaskContainers(name=container_name, type=container_type))
        return containers

    def _parse_args(
        self, name: str, config: OnefuzzTemplateConfig, args: Any
    ) -> OnefuzzTemplateRequest:
        """ convert arguments from argparse into a OnefuzzTemplateRequest """
        user_fields = {}
        for field in config.user_fields:
            if field.name not in args and field.required:
                raise Exception("missing field: %s" % field.name)
            value = args[field.name]
            if value is not None:
                user_fields[field.name] = value

        containers = self._parse_container_args(config, args)
        for container in containers:
            container_name = container_type_name(container.type)
            if container_name in args and args[container_name] is not None:
                print("upload %s to %s" % (args[container_name], container.name))

        request = OnefuzzTemplateRequest(
            template_name=name, user_fields=user_fields, containers=containers
        )
        return request

    def _submit_request(self, request: OnefuzzTemplateRequest) -> int:
        self.logger.warning(
            "do something with the request here... %s", request.json(indent=4)
        )
        return 3

    def _execute(
        self,
        name: str,
        config: OnefuzzTemplateConfig,
        args: Any,
    ) -> int:
        self.logger.debug("building: %s", name)
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

    for container in config.containers:
        if container not in ContainerType.user_config():
            continue

        param = Parameter(
            container_type_name(container),
            Parameter.KEYWORD_ONLY,
            annotation=Optional[Directory],
            default=Parameter.empty,
        )
        params.append(param)

    if config.containers:
        param = Parameter(
            "container_names",
            Parameter.KEYWORD_ONLY,
            annotation=Optional[Dict[ContainerType, str]],
            default=None,
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
