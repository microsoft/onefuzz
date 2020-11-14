#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import datetime
import logging
from inspect import Parameter, signature
from typing import Any, Dict, List, Optional

from onefuzztypes.enums import ContainerType, UserFieldType
from onefuzztypes.job_templates import JobTemplateConfig, JobTemplateRequest
from onefuzztypes.models import Job, TaskContainers
from onefuzztypes.primitives import Directory, File

from ..api import Endpoint, Onefuzz
from ..templates import _build_container_name
from .cache import CachedTemplates
from .manage import Manage

LOGGER = logging.getLogger("job-templates")


def container_type_name(container_type: ContainerType) -> str:
    return container_type.name + "_dir"


class TemplateHandler(Endpoint):
    """ Submit job template """

    endpoint = "job_templates"

    def _convert_container_args(
        self, config: JobTemplateConfig, args: Any
    ) -> List[TaskContainers]:
        """ Convert the job template into a list of containers """

        containers = []
        container_names = args["container_names"]
        if container_names is None:
            container_names = {}
        for container_type in config.containers:
            if container_type.name in container_names:
                container_name = args["container_names"][container_type]
            else:
                container_name = _build_container_name(
                    self.onefuzz,
                    container_type,
                    args["project"],
                    args["name"],
                    args["build"],
                    config.os,
                )
            containers.append(TaskContainers(name=container_name, type=container_type))
        return containers

    def _convert_args(self, config: JobTemplateConfig, args: Any) -> JobTemplateRequest:
        """ convert arguments from argparse into a JobTemplateRequest """

        user_fields = {}
        for field in config.user_fields:
            if field.name not in args and field.required:
                raise Exception("missing field: %s" % field.name)
            value = args[field.name]
            if value is not None:
                user_fields[field.name] = value

        containers = self._convert_container_args(config, args)
        for container in containers:
            directory_arg = container_type_name(container.type)

            self.onefuzz.containers.create(
                container.name, metadata={"container_type": container.type.name}
            )

            if directory_arg in args and args[directory_arg] is not None:
                self.onefuzz.containers.files.upload_dir(
                    container.name, args[directory_arg]
                )

        request = JobTemplateRequest(
            name=config.name, user_fields=user_fields, containers=containers
        )
        return request

    def _process_containers(self, request: JobTemplateRequest, args: Any) -> None:
        """ Create containers based on the argparse args """

        for container in request.containers:
            directory_arg = container_type_name(container.type)
            self.onefuzz.logger.info("creating container: %s", container.name)
            self.onefuzz.containers.create(
                container.name, metadata={"container_type": container.type.name}
            )

            if directory_arg in args and args[directory_arg] is not None:
                self.onefuzz.logger.info(
                    "uploading %s to %s", args[directory_arg], container.name
                )
                self.onefuzz.containers.files.upload_dir(
                    container.name, args[directory_arg]
                )
            elif container.type == ContainerType.setup and "target_exe" in args:
                # This is isn't "declarative", but models our existing paths for
                # templates.

                target_exe = args["target_exe"]
                if target_exe is None:
                    continue
                self.onefuzz.logger.info(
                    "uploading %s to %s", target_exe, container.name
                )
                self.onefuzz.containers.files.upload_file(container.name, target_exe)

    def _execute(
        self,
        config: JobTemplateConfig,
        args: Any,
    ) -> Job:
        """ Convert argparse args into a JobTemplateRequest and submit it """

        self.onefuzz.logger.debug("building: %s", config.name)
        request = self._convert_args(config, args)
        self._process_containers(request, args)
        return self._req_model("POST", Job, data=request)


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


def config_to_params(config: JobTemplateConfig) -> List[Parameter]:
    params: List[Parameter] = []

    for entry in config.user_fields:
        is_optional = entry.default is None and entry.required is False
        if entry.name == "target_exe":
            annotation = Optional[File] if is_optional else File
        else:
            annotation = (
                Optional[TYPES[entry.type]] if is_optional else TYPES[entry.type]
            )

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
    def func(self: TemplateHandler, **kwargs: Any) -> Job:
        return self._execute(config, kwargs)

    sig = signature(func)
    params = [sig.parameters["self"]] + config_to_params(config)
    sig = sig.replace(parameters=tuple(params))
    func.__signature__ = sig  # type: ignore

    func.__doc__ = build_template_doc(config)

    return func


def load_templates(templates: List[JobTemplateConfig]) -> None:
    for name in dir(TemplateHandler):
        if name.startswith("_"):
            continue
        delattr(TemplateHandler, name)

    for template in templates:
        setattr(TemplateHandler, template.name, build_template_func(template))


class JobTemplates(Endpoint):
    """ Job Templates """

    endpoint = "job_templates"

    def __init__(self, onefuzz: Onefuzz):
        super().__init__(onefuzz)
        self.manage = Manage(onefuzz)
        self.submit = TemplateHandler(onefuzz)

    def _load_cache(self) -> None:
        endpoint = self.onefuzz._backend.config.get("endpoint")
        if endpoint is None:
            return

        yesterday = datetime.datetime.utcnow() - datetime.timedelta(hours=24)
        entry = CachedTemplates.get(endpoint)
        if not entry or entry.timestamp < yesterday:
            self.refresh()
            return

        load_templates(entry.configs)

    def refresh(self) -> None:
        """ Update available templates """
        self.onefuzz.logger.info("refreshing job template cache")

        templates = self._req_model_list("GET", JobTemplateConfig)

        for template in templates:
            self.onefuzz.logger.info("updated template definition: %s", template.name)

        CachedTemplates.add(self.onefuzz._backend.config["endpoint"], templates)

        load_templates(templates)
