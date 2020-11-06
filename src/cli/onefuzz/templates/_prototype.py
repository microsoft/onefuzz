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

from onefuzztypes.enums import OS, ContainerType, UserFieldType
from onefuzztypes.models import (
    Job,
    OnefuzzTemplateConfig,
    OnefuzzTemplateRequest,
    Task,
    TaskContainers,
)
from onefuzztypes.primitives import Directory, File

from onefuzz.api import Command

from . import _build_container_name
from ._render_template import build_input_config, render
from ._usertemplates import TEMPLATES

# from . import JobHelper


def container_type_name(container_type: ContainerType) -> str:
    return container_type.name + "_dir"


class Prototype(Command):
    """ Pre-defined Prototype job """

    def _convert_container_args(
        self, config: OnefuzzTemplateConfig, args: Any, platform: OS
    ) -> List[TaskContainers]:
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
                    platform,
                )
            containers.append(TaskContainers(name=container_name, type=container_type))
        return containers

    def _convert_args(
        self, template_name: str, config: OnefuzzTemplateConfig, args: Any, platform: OS
    ) -> OnefuzzTemplateRequest:
        """ convert arguments from argparse into a OnefuzzTemplateRequest """
        user_fields = {}
        for field in config.user_fields:
            if field.name not in args and field.required:
                raise Exception("missing field: %s" % field.name)
            value = args[field.name]
            if value is not None:
                user_fields[field.name] = value

        containers = self._convert_container_args(config, args, platform)
        for container in containers:
            directory_arg = container_type_name(container.type)

            self.onefuzz.containers.create(
                container.name, metadata={"container_type": container.type.name}
            )

            if directory_arg in args and args[directory_arg] is not None:
                self.onefuzz.containers.files.upload_dir(
                    container.name, args[directory_arg]
                )

        request = OnefuzzTemplateRequest(
            template_name=template_name, user_fields=user_fields, containers=containers
        )
        return request

    def _process_containers(self, request: OnefuzzTemplateRequest, args: Any) -> None:
        for container in request.containers:
            directory_arg = container_type_name(container.type)
            self.logger.info("creating container: %s", container.name)
            self.onefuzz.containers.create(
                container.name, metadata={"container_type": container.type.name}
            )

            if directory_arg in args and args[directory_arg] is not None:
                self.logger.info(
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
                self.logger.info("uploading %s to %s", target_exe, container.name)
                self.onefuzz.containers.files.upload_file(container.name, target_exe)

    def _submit_request(self, request: OnefuzzTemplateRequest) -> Job:
        # In the POC, this is done locally. In production, the request will be
        # submitted to the server, which would do the same thing.
        config = render(request, TEMPLATES[request.template_name])

        for template_notification in config.notifications:
            for task_container in request.containers:
                if task_container.type == template_notification.container_type:
                    self.logger.info("creating notification: %s", task_container.name)
                    self.onefuzz.notifications.create(
                        task_container.name, template_notification.notification
                    )

        job = self.onefuzz.jobs.create_with_config(config.job)
        self.logger.info("created job: %s", job.job_id)
        tasks: List[Task] = []
        for task_config in config.tasks:
            if task_config.pool is None:
                raise Exception("pool not defined")
            task_config.job_id = job.job_id
            if task_config.prereq_tasks:
                # the model checker verifies prereq_tasks in u128 form are index refs to
                # previously generated tasks
                task_config.prereq_tasks = [
                    tasks[x.int].task_id for x in task_config.prereq_tasks
                ]
            self.logger.info(
                "creating task. pool:%s type:%s",
                task_config.pool.pool_name,
                task_config.task.type.name,
            )
            task = self.onefuzz.tasks.create_with_config(task_config)
            tasks.append(task)

        return job

    def _execute(
        self,
        template_name: str,
        config: OnefuzzTemplateConfig,
        platform: OS,
        args: Any,
    ) -> Job:
        self.logger.debug("building: %s", template_name)
        request = self._convert_args(template_name, config, args, platform)
        self._process_containers(request, args)
        result = self._submit_request(request)
        return result


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


def config_to_params(config: OnefuzzTemplateConfig) -> List[Parameter]:
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


def build_template_doc(name: str, config: OnefuzzTemplateConfig) -> str:
    docs = [
        f"Launch a pre-defined {name} job",
        "",
        ":param Platform platform: Specify the OS to use in the job.",
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


def build_template_func(name: str, config: OnefuzzTemplateConfig) -> Any:
    def func(self: Prototype, platform: OS, **kwargs: Any) -> Job:
        return self._execute(name, config, platform, kwargs)

    sig = signature(func)
    params = [sig.parameters["self"], sig.parameters["platform"]] + config_to_params(
        config
    )
    sig = sig.replace(parameters=tuple(params))
    func.__signature__ = sig  # type: ignore

    func.__doc__ = build_template_doc(name, config)

    return func


# For now, these come from a local python file.  Eventually, we would expect these
# to come from a server API, with local caching
for name, template in TEMPLATES.items():
    config = build_input_config(template)
    setattr(Prototype, name, build_template_func(name, config))
