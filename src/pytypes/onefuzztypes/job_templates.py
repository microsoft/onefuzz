#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from typing import Dict, List, Optional, Union

from pydantic import BaseModel, Field, root_validator, validator

from ._monkeypatch import _check_hotfix
from .enums import OS, ContainerType, UserFieldOperation, UserFieldType
from .models import JobConfig, NotificationConfig, TaskConfig, TaskContainers
from .primitives import File
from .requests import BaseRequest
from .responses import BaseResponse
from .validators import check_template_name, check_template_name_optional


class UserFieldLocation(BaseModel):
    op: UserFieldOperation
    path: str


TemplateUserData = Union[bool, int, str, Dict[str, str], List[str], File]
TemplateUserFields = Dict[str, TemplateUserData]


class UserField(BaseModel):
    name: str
    type: UserFieldType
    locations: List[UserFieldLocation]
    required: bool = Field(default=False)
    default: Optional[TemplateUserData]
    help: str

    @validator("locations", allow_reuse=True)
    def check_locations(cls, value: List) -> List:
        if len(value) == 0:
            raise ValueError("must provide at least one location")
        return value


class JobTemplateNotification(BaseModel):
    container_type: ContainerType
    notification: NotificationConfig


class JobTemplate(BaseResponse):
    os: OS
    job: JobConfig
    tasks: List[TaskConfig]
    notifications: List[JobTemplateNotification]
    user_fields: List[UserField]

    @root_validator()
    def check_task_prereqs(cls, data: Dict) -> Dict:
        for idx, task in enumerate(data["tasks"]):
            # prereq_tasks must refer to previously defined tasks, using the u128
            #  representation of the UUID as an index
            if task.prereq_tasks:
                for prereq in task.prereq_tasks:
                    if prereq.int >= idx:
                        raise Exception(f"invalid task reference: {idx} - {prereq}")
        return data

    @root_validator()
    def check_fields(cls, data: Dict) -> Dict:
        seen = set()
        seen_path = set()

        for entry in TEMPLATE_BASE_FIELDS + data["user_fields"]:
            # field names, which are sent to the user for filling out, must be
            # specified once and only once
            if entry.name in seen:
                raise Exception(f"duplicate field found: {entry.name}")
            seen.add(entry.name)

            # location.path, the location in the json doc that is modified,
            # must be specified once and only once
            for location in entry.locations:
                if location.path in seen_path:
                    raise Exception(f"duplicate path found: {location.path}")
                seen_path.add(location.path)

            if entry.name in ["platform"]:
                raise Exception(f"reserved field name: {entry.name}")

        return data


class JobTemplateIndex(BaseResponse):
    name: str
    template: JobTemplate

    _validate_name: classmethod = validator("name", allow_reuse=True)(
        check_template_name
    )


class JobTemplateField(BaseModel):
    name: str
    help: str
    type: UserFieldType
    required: bool
    default: Optional[TemplateUserData]


class JobTemplateConfig(BaseResponse):
    os: OS
    name: str
    user_fields: List[JobTemplateField]
    containers: List[ContainerType]


TEMPLATE_BASE_FIELDS = [
    UserField(
        name="project",
        help="Name of the Project",
        type=UserFieldType.Str,
        required=True,
        locations=[
            UserFieldLocation(
                op=UserFieldOperation.replace,
                path="/job/project",
            ),
        ],
    ),
    UserField(
        name="name",
        help="Name of the Target",
        type=UserFieldType.Str,
        required=True,
        locations=[
            UserFieldLocation(
                op=UserFieldOperation.replace,
                path="/job/name",
            ),
        ],
    ),
    UserField(
        name="build",
        help="Name of the Target",
        type=UserFieldType.Str,
        required=True,
        locations=[
            UserFieldLocation(
                op=UserFieldOperation.replace,
                path="/job/build",
            ),
        ],
    ),
]


class JobTemplateUpload(BaseRequest):
    name: str
    template: JobTemplate

    _verify_name: classmethod = validator("name", allow_reuse=True)(check_template_name)


class JobTemplateDelete(BaseRequest):
    name: str

    _verify_name: classmethod = validator("name", allow_reuse=True)(check_template_name)


class JobTemplateRequest(BaseRequest):
    name: str
    user_fields: TemplateUserFields
    containers: List[TaskContainers]

    _validate_name: classmethod = validator("name", allow_reuse=True)(
        check_template_name
    )


class JobTemplateGet(BaseRequest):
    name: Optional[str]

    _validate_name: classmethod = validator("name", allow_reuse=True)(
        check_template_name_optional
    )


class JobTemplateRequestParameters(BaseRequest):
    user_fields: TemplateUserFields


_check_hotfix()
