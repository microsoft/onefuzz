#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from inspect import Parameter, signature
from typing import Any, Dict, List, Optional

from onefuzztypes.enums import ContainerType, UserFieldType
from onefuzztypes.job_templates import JobTemplateConfig, JobTemplateRequestParameters
from onefuzztypes.models import Job
from onefuzztypes.primitives import Directory, File

from .handlers import TemplateSubmitHandler

LOGGER = logging.getLogger("job-templates")

TYPES = {
    UserFieldType.Str: str,
    UserFieldType.Int: int,
    UserFieldType.ListStr: List[str],
    UserFieldType.DictStr: Dict[str, str],
    UserFieldType.Bool: bool,
}

NAMES = {
    UserFieldType.Str: "str",
    UserFieldType.Int: "int",
    UserFieldType.ListStr: "list",
    UserFieldType.DictStr: "dict",
    UserFieldType.Bool: "bool",
}


def container_type_name(container_type: ContainerType) -> str:
    return container_type.name + "_dir"


def config_to_params(config: JobTemplateConfig) -> List[Parameter]:
    params: List[Parameter] = []

    for entry in config.user_fields:
        default = entry.default if entry.default is not None else Parameter.empty
        annotation: Any = None

        if entry.name == "target_exe":
            annotation = File
        else:
            annotation = TYPES[entry.type]

        is_optional = entry.default is None and entry.required is False
        if is_optional:
            annotation = Optional[annotation]

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

    param = Parameter(
        "parameters",
        Parameter.KEYWORD_ONLY,
        annotation=Optional[JobTemplateRequestParameters],
        default=Parameter.empty,
    )
    params.append(param)

    return params


def build_template_doc(config: JobTemplateConfig) -> str:
    docs = [
        f"Launch '{config.name}' job",
        "",
    ]

    for entry in config.user_fields:
        line = f":param {NAMES[entry.type]} {entry.name}: {entry.help}"
        docs.append(line)

    for container in config.containers:
        if container not in ContainerType.user_config():
            continue
        line = f":param Directory {container_type_name(container)}: Local path to the {container.name} directory"
        docs.append(line)

    if config.containers:
        line = ":param dict container_names: custom container names (eg: setup=my-setup-container)"
        docs.append(line)

    return "\n".join(docs)


def build_template_func(config: JobTemplateConfig) -> Any:
    def func(self: TemplateSubmitHandler, **kwargs: Any) -> Job:
        return self._execute(config, kwargs)

    sig = signature(func)
    params = [sig.parameters["self"]] + config_to_params(config)
    sig = sig.replace(parameters=tuple(params))
    func.__signature__ = sig  # type: ignore

    func.__doc__ = build_template_doc(config)

    return func
