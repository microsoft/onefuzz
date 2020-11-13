import string
from uuid import uuid4, UUID
from typing import Dict, List, Optional, Union

from pydantic import BaseModel, Field, root_validator, validator

from .enums import ContainerType, UserFieldOperation, UserFieldType
from .models import JobConfig, NotificationConfig, TaskConfig, TaskContainers
from .primitives import File
from .validators import check_template_name, check_template_name_modify
from .requests import BaseRequest
from .responses import BaseResponse


class UserFieldLocation(BaseModel):
    op: UserFieldOperation
    path: str


TemplateUserData = Union[bool, int, str, Dict[str, str], List[str], File]


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


class JobTemplate(BaseModel):
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
            # field names, which are sent to the user for filing out, must be specified
            # once and only once
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
    domain: str
    name: str
    template: JobTemplate

    _validate_domain = validator("domain", allow_reuse=True)(check_template_name)
    _validate_name = validator("name", allow_reuse=True)(check_template_name)


class JobTemplateField(BaseModel):
    name: str
    help: str
    type: UserFieldType
    required: bool
    default: Optional[TemplateUserData]


class JobTemplateConfig(BaseModel):
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


class JobTemplateCreate(BaseRequest):
    domain: str
    name: str
    template: JobTemplate

    _verify_domain = validator("domain", allow_reuse=True)(check_template_name_modify)
    _verify_name = validator("name", allow_reuse=True)(check_template_name_modify)


class JobTemplateDelete(BaseRequest):
    domain: str
    name: str

    _verify_domain = validator("domain", allow_reuse=True)(check_template_name_modify)
    _verify_name = validator("name", allow_reuse=True)(check_template_name_modify)


class JobTemplateUpdate(BaseRequest):
    domain: str
    name: str
    template: JobTemplate

    _verify_domain = validator("domain", allow_reuse=True)(check_template_name_modify)
    _verify_name = validator("name", allow_reuse=True)(check_template_name_modify)


class JobTemplateRequest(BaseRequest):
    domain: str
    name: str
    user_fields: Dict[str, TemplateUserData]
    containers: List[TaskContainers]

    _validate_domain = validator("template_domain", allow_reuse=True)(
        check_template_name
    )
    _validate_name = validator("template_name", allow_reuse=True)(check_template_name)
