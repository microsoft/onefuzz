#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
from typing import Dict, List

from jsonpatch import apply_patch
from memoization import cached
from onefuzztypes.models import Error, Result
from onefuzztypes.enums import ContainerType, UserFieldType, ErrorCode
from onefuzztypes.job_templates import (
    TEMPLATE_BASE_FIELDS,
    JobTemplate,
    JobTemplateConfig,
    JobTemplateField,
    JobTemplateRequest,
    TemplateUserData,
    UserField,
)


def template_container_types(template: JobTemplate) -> List[ContainerType]:
    return list(set(y.type for x in template.tasks for y in x.containers if not y.name))


@cached
def build_input_config(name: str, template: JobTemplate) -> JobTemplateConfig:
    user_fields = [
        JobTemplateField(
            name=x.name,
            type=x.type,
            required=x.required,
            default=x.default,
            help=x.help,
        )
        for x in TEMPLATE_BASE_FIELDS + template.user_fields
    ]
    containers = template_container_types(template)

    return JobTemplateConfig(
        os=template.os,
        name=name,
        user_fields=user_fields,
        containers=containers,
    )


def build_patches(
    data: TemplateUserData, field: UserField
) -> List[Dict[str, TemplateUserData]]:
    patches = []

    if field.type == UserFieldType.Bool and not isinstance(data, bool):
        raise Exception("invalid bool field")
    if field.type == UserFieldType.Int and not isinstance(data, int):
        raise Exception("invalid int field")
    if field.type == UserFieldType.Str and not isinstance(data, str):
        raise Exception("invalid str field")
    if field.type == UserFieldType.DictStr and not isinstance(data, dict):
        raise Exception("invalid DictStr field")
    if field.type == UserFieldType.ListStr and not isinstance(data, list):
        raise Exception("invalid ListStr field")

    for location in field.locations:
        patches.append(
            {
                "op": location.op.name,
                "path": location.path,
                "value": data,
            }
        )

    return patches


def _fail(why: str) -> Error:
    return Error(code=ErrorCode.INVALID_REQUEST, errors=[why])


def render(request: JobTemplateRequest, template: JobTemplate) -> Result[JobTemplate]:
    patches = []
    seen = set()

    for name in request.user_fields:
        for field in TEMPLATE_BASE_FIELDS + template.user_fields:
            if field.name == name:
                if name in seen:
                    return _fail(f"duplicate specification: {name}")

                seen.add(name)

        if name not in seen:
            return _fail(f"extra field: {name}")

    for field in TEMPLATE_BASE_FIELDS + template.user_fields:
        if field.name not in request.user_fields:
            if field.required:
                return _fail(f"missing required field: {field.name}")
            else:
                continue
        patches += build_patches(request.user_fields[field.name], field)

    raw = json.loads(template.json())
    updated = apply_patch(raw, patches)
    rendered = JobTemplate.parse_obj(updated)

    used_containers = []
    for task in rendered.tasks:
        for task_container in task.containers:
            if task_container.name:
                continue

            for entry in request.containers:
                if entry.type != task_container.type:
                    continue
                task_container.name = entry.name
                used_containers.append(entry)

            if not task_container.name:
                return _fail(f"missing container definition {task_container.type}")

    for entry in request.containers:
        if entry not in used_containers:
            return _fail(f"unused container in request: {entry}")

    return rendered
