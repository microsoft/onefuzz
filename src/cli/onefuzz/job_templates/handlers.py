#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from typing import Any, Dict, List

from onefuzztypes.enums import ContainerType
from onefuzztypes.job_templates import JobTemplateConfig, JobTemplateRequest
from onefuzztypes.models import Job, TaskContainers

from ..api import Endpoint
from ..templates import _build_container_name


def container_type_name(container_type: ContainerType) -> str:
    return container_type.name + "_dir"


class TemplateSubmitHandler(Endpoint):
    """ Submit Job Template """

    _endpoint = "job_templates"

    def _process_containers(
        self, request: JobTemplateRequest, args: Dict[str, Any]
    ) -> None:
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

    def _define_missing_containers(
        self, config: JobTemplateConfig, request: JobTemplateRequest
    ) -> None:

        for container_type in config.containers:
            seen = False
            for container in request.containers:
                if container_type == container.type:
                    seen = True
            if not seen:
                assert isinstance(request.user_fields["project"], str)
                assert isinstance(request.user_fields["name"], str)
                assert isinstance(request.user_fields["build"], str)
                container_name = _build_container_name(
                    self.onefuzz,
                    container_type,
                    request.user_fields["project"],
                    request.user_fields["name"],
                    request.user_fields["build"],
                    config.os,
                )
                request.containers.append(
                    TaskContainers(name=container_name, type=container_type)
                )

    def _submit(self, request: JobTemplateRequest) -> Job:
        return self._req_model(
            "POST", Job, data=request, alternate_endpoint=self._endpoint
        )

    def _execute_request(
        self,
        config: JobTemplateConfig,
        request: JobTemplateRequest,
        args: Dict[str, Any],
    ) -> Job:
        self._define_missing_containers(config, request)
        self._process_containers(request, args)
        return self._submit(request)

    def _convert_container_args(
        self, config: JobTemplateConfig, args: Dict[str, Any]
    ) -> List[TaskContainers]:
        """ Convert the job template into a list of containers """

        containers = []
        container_names = args["container_names"]
        if container_names is None:
            container_names = {}
        for container_type in config.containers:
            if container_type.name in container_names:
                container_name = args["container_names"][container_type]
                containers.append(
                    TaskContainers(name=container_name, type=container_type)
                )
        return containers

    def _convert_args(
        self, config: JobTemplateConfig, args: Dict[str, Any]
    ) -> JobTemplateRequest:
        """ convert arguments from argparse into a JobTemplateRequest """

        user_fields = {}
        for field in config.user_fields:
            value = None
            if field.name in args:
                value = args[field.name]
            elif field.name in args["parameters"]:
                value = args["parameters"][field.name]
            elif field.required:
                raise Exception("missing field: %s" % field.name)

            if value is not None:
                user_fields[field.name] = value

        containers = self._convert_container_args(config, args)

        request = JobTemplateRequest(
            name=config.name, user_fields=user_fields, containers=containers
        )
        return request

    def _execute(
        self,
        config: JobTemplateConfig,
        args: Dict[str, Any],
    ) -> Job:
        """ Convert argparse args into a JobTemplateRequest and submit it """
        self.onefuzz.logger.debug("building: %s", config.name)
        request = self._convert_args(config, args)
        return self._execute_request(config, request, args)
