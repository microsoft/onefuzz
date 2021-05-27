#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import inspect
import json
import logging
from datetime import datetime
from enum import Enum
from typing import (
    Any,
    Callable,
    Dict,
    List,
    Mapping,
    Optional,
    Tuple,
    Type,
    TypeVar,
    Union,
)
from uuid import UUID

from azure.common import AzureConflictHttpError, AzureMissingResourceHttpError
from onefuzztypes.enums import (
    ErrorCode,
    JobState,
    NodeState,
    PoolState,
    ScalesetState,
    TaskState,
    TelemetryEvent,
    UpdateType,
    VmState,
)
from onefuzztypes.models import Error, SecretData
from onefuzztypes.primitives import Container, PoolName, Region
from pydantic import BaseModel
from typing_extensions import Protocol

from .azure.table import get_client
from .secrets import save_to_keyvault
from .telemetry import track_event_filtered
from .updates import queue_update

A = TypeVar("A", bound="ORMMixin")

QUERY_VALUE_TYPES = Union[
    List[bool],
    List[int],
    List[str],
    List[UUID],
    List[Region],
    List[Container],
    List[PoolName],
    List[VmState],
    List[ScalesetState],
    List[JobState],
    List[TaskState],
    List[PoolState],
    List[NodeState],
]
QueryFilter = Dict[str, QUERY_VALUE_TYPES]

SAFE_STRINGS = (UUID, Container, Region, PoolName)
KEY = Union[int, str, UUID, Enum]

HOURS = 60 * 60


class HasState(Protocol):
    # TODO: this should be bound tighter than Any
    # In the end, we want this to be an Enum.  Specifically, one of
    # the JobState,TaskState,etc enums.
    state: Any

    def get_keys(self) -> Tuple[KEY, KEY]:
        ...


def process_state_update(obj: HasState) -> None:
    """
    process a single state update, if the obj
    implements a function for that state
    """

    func = getattr(obj, obj.state.name, None)
    if func is None:
        return

    keys = obj.get_keys()

    logging.info(
        "processing state update: %s - %s - %s", type(obj), keys, obj.state.name
    )
    func()


def process_state_updates(obj: HasState, max_updates: int = 5) -> None:
    """process through the state machine for an object"""

    for _ in range(max_updates):
        state = obj.state
        process_state_update(obj)
        new_state = obj.state
        if new_state == state:
            break


def resolve(key: KEY) -> str:
    if isinstance(key, str):
        return key
    elif isinstance(key, UUID):
        return str(key)
    elif isinstance(key, Enum):
        return key.name
    elif isinstance(key, int):
        return str(key)
    raise NotImplementedError("unsupported type %s - %s" % (type(key), repr(key)))


def build_filters(
    cls: Type[A], query_args: Optional[QueryFilter]
) -> Tuple[Optional[str], QueryFilter]:
    if not query_args:
        return (None, {})

    partition_key_field, row_key_field = cls.key_fields()

    search_filter_parts = []
    post_filters: QueryFilter = {}

    for field, values in query_args.items():
        if field not in cls.__fields__:
            raise ValueError("unexpected field %s: %s" % (repr(field), cls))

        if not values:
            continue

        if field == partition_key_field:
            field_name = "PartitionKey"
        elif field == row_key_field:
            field_name = "RowKey"
        else:
            field_name = field

        parts: Optional[List[str]] = None
        if isinstance(values[0], bool):
            parts = []
            for x in values:
                if not isinstance(x, bool):
                    raise TypeError("unexpected type")
                parts.append("%s eq %s" % (field_name, str(x).lower()))
        elif isinstance(values[0], int):
            parts = []
            for x in values:
                if not isinstance(x, int):
                    raise TypeError("unexpected type")
                parts.append("%s eq %d" % (field_name, x))
        elif isinstance(values[0], Enum):
            parts = []
            for x in values:
                if not isinstance(x, Enum):
                    raise TypeError("unexpected type")
                parts.append("%s eq '%s'" % (field_name, x.name))
        elif all(isinstance(x, SAFE_STRINGS) for x in values):
            parts = ["%s eq '%s'" % (field_name, x) for x in values]
        else:
            post_filters[field_name] = values

        if parts:
            if len(parts) == 1:
                search_filter_parts.append(parts[0])
            else:
                search_filter_parts.append("(" + " or ".join(parts) + ")")

    if search_filter_parts:
        return (" and ".join(search_filter_parts), post_filters)

    return (None, post_filters)


def post_filter(value: Any, filters: Optional[QueryFilter]) -> bool:
    if not filters:
        return True

    for field in filters:
        if field not in value:
            return False
        if value[field] not in filters[field]:
            return False

    return True


MappingIntStrAny = Mapping[Union[int, str], Any]
# A = TypeVar("A", bound="Model")


class ModelMixin(BaseModel):
    def export_exclude(self) -> Optional[MappingIntStrAny]:
        return None

    def raw(
        self,
        *,
        by_alias: bool = False,
        exclude_none: bool = False,
        exclude: MappingIntStrAny = None,
        include: MappingIntStrAny = None,
    ) -> Dict[str, Any]:
        # cycling through json means all wrapped types get resolved, such as UUID
        result: Dict[str, Any] = json.loads(
            self.json(
                by_alias=by_alias,
                exclude_none=exclude_none,
                exclude=exclude,
                include=include,
            )
        )
        return result


B = TypeVar("B", bound=BaseModel)


def hide_secrets(data: B, hider: Callable[[SecretData], SecretData]) -> B:
    for field in data.__fields__:
        field_data = getattr(data, field)

        if isinstance(field_data, SecretData):
            field_data = hider(field_data)
        elif isinstance(field_data, BaseModel):
            field_data = hide_secrets(field_data, hider)
        elif isinstance(field_data, list):
            field_data = [
                hide_secrets(x, hider) if isinstance(x, BaseModel) else x
                for x in field_data
            ]
        elif isinstance(field_data, dict):
            for key in field_data:
                if not isinstance(field_data[key], BaseModel):
                    continue
                field_data[key] = hide_secrets(field_data[key], hider)

        setattr(data, field, field_data)

    return data


# NOTE: if you want to include Timestamp in a model that uses ORMMixin,
# it must be maintained as part of the model.
class ORMMixin(ModelMixin):
    etag: Optional[str]

    @classmethod
    def table_name(cls: Type[A]) -> str:
        return cls.__name__

    @classmethod
    def get(
        cls: Type[A], PartitionKey: KEY, RowKey: Optional[KEY] = None
    ) -> Optional[A]:
        client = get_client(table=cls.table_name())
        partition_key = resolve(PartitionKey)
        row_key = resolve(RowKey) if RowKey else partition_key

        try:
            raw = client.get_entity(cls.table_name(), partition_key, row_key)
        except AzureMissingResourceHttpError:
            return None
        return cls.load(raw)

    @classmethod
    def key_fields(cls) -> Tuple[str, Optional[str]]:
        raise NotImplementedError("keys not defined")

    # FILTERS:
    # The following
    # * save_exclude: Specify fields to *exclude* from saving to Storage Tables
    # * export_exclude: Specify the fields to *exclude* from sending to an external API
    # * telemetry_include: Specify the fields to *include* for telemetry
    #
    # For implementation details see:
    # https://pydantic-docs.helpmanual.io/usage/exporting_models/#advanced-include-and-exclude
    def save_exclude(self) -> Optional[MappingIntStrAny]:
        return None

    def export_exclude(self) -> Optional[MappingIntStrAny]:
        return {"etag": ...}

    def telemetry_include(self) -> Optional[MappingIntStrAny]:
        return {}

    def telemetry(self) -> Any:
        return self.raw(exclude_none=True, include=self.telemetry_include())

    def get_keys(self) -> Tuple[KEY, KEY]:
        partition_key_field, row_key_field = self.key_fields()

        partition_key = getattr(self, partition_key_field)
        if row_key_field:
            row_key = getattr(self, row_key_field)
        else:
            row_key = partition_key

        return (partition_key, row_key)

    def save(self, new: bool = False, require_etag: bool = False) -> Optional[Error]:
        self = hide_secrets(self, save_to_keyvault)
        # TODO: migrate to an inspect.signature() model
        raw = self.raw(by_alias=True, exclude_none=True, exclude=self.save_exclude())
        for key in raw:
            if not isinstance(raw[key], (str, int)):
                raw[key] = json.dumps(raw[key])

        for field in self.__fields__:
            if field not in raw:
                continue
            # for datetime fields that passed through filtering, use the real value,
            # rather than a serialized form
            if self.__fields__[field].type_ == datetime:
                raw[field] = getattr(self, field)

        partition_key_field, row_key_field = self.key_fields()

        # PartitionKey and RowKey must be 'str'
        raw["PartitionKey"] = resolve(raw[partition_key_field])
        raw["RowKey"] = resolve(raw[row_key_field or partition_key_field])

        del raw[partition_key_field]
        if row_key_field in raw:
            del raw[row_key_field]

        client = get_client(table=self.table_name())

        # never save the timestamp
        if "Timestamp" in raw:
            del raw["Timestamp"]

        if new:
            try:
                self.etag = client.insert_entity(self.table_name(), raw)
            except AzureConflictHttpError:
                return Error(
                    code=ErrorCode.UNABLE_TO_CREATE, errors=["entry already exists"]
                )
        elif self.etag and require_etag:
            self.etag = client.replace_entity(
                self.table_name(), raw, if_match=self.etag
            )
        else:
            self.etag = client.insert_or_replace_entity(self.table_name(), raw)

        if self.table_name() in TelemetryEvent.__members__:
            telem = self.telemetry()
            if telem:
                track_event_filtered(TelemetryEvent[self.table_name()], telem)

        return None

    def delete(self) -> None:
        partition_key, row_key = self.get_keys()

        client = get_client()
        try:
            client.delete_entity(
                self.table_name(), resolve(partition_key), resolve(row_key)
            )
        except AzureMissingResourceHttpError:
            # It's OK if the component is already deleted
            pass

    @classmethod
    def load(cls: Type[A], data: Dict[str, Union[str, bytes, bytearray]]) -> A:
        partition_key_field, row_key_field = cls.key_fields()

        if partition_key_field in data:
            raise Exception(
                "duplicate PartitionKey field %s for %s"
                % (partition_key_field, cls.table_name())
            )
        if row_key_field in data:
            raise Exception(
                "duplicate RowKey field %s for %s" % (row_key_field, cls.table_name())
            )

        data[partition_key_field] = data["PartitionKey"]
        if row_key_field is not None:
            data[row_key_field] = data["RowKey"]

        del data["PartitionKey"]
        del data["RowKey"]

        for key in inspect.signature(cls).parameters:
            if key not in data:
                continue

            annotation = inspect.signature(cls).parameters[key].annotation

            if inspect.isclass(annotation):
                if (
                    issubclass(annotation, BaseModel)
                    or issubclass(annotation, dict)
                    or issubclass(annotation, list)
                ):
                    data[key] = json.loads(data[key])
                    continue

            if getattr(annotation, "__origin__", None) == Union and any(
                inspect.isclass(x) and issubclass(x, BaseModel)
                for x in annotation.__args__
            ):
                data[key] = json.loads(data[key])
                continue

            # Required for Python >=3.7. In 3.6, a `Dict[_,_]` and `List[_]` annotations
            # are a class according to `inspect.isclass`.
            if getattr(annotation, "__origin__", None) in [dict, list]:
                data[key] = json.loads(data[key])
                continue

        return cls.parse_obj(data)

    @classmethod
    def search(
        cls: Type[A],
        *,
        query: Optional[QueryFilter] = None,
        raw_unchecked_filter: Optional[str] = None,
        num_results: int = None,
    ) -> List[A]:
        search_filter, post_filters = build_filters(cls, query)

        if raw_unchecked_filter is not None:
            if search_filter is None:
                search_filter = raw_unchecked_filter
            else:
                search_filter = "(%s) and (%s)" % (search_filter, raw_unchecked_filter)

        client = get_client(table=cls.table_name())
        entries = []
        for row in client.query_entities(
            cls.table_name(), filter=search_filter, num_results=num_results
        ):
            if not post_filter(row, post_filters):
                continue

            entry = cls.load(row)
            entries.append(entry)
        return entries

    def queue(
        self,
        *,
        method: Optional[Callable] = None,
        visibility_timeout: Optional[int] = None,
    ) -> None:
        if not hasattr(self, "state"):
            raise NotImplementedError("Queued an ORM mapping without State")

        update_type = UpdateType.__members__.get(type(self).__name__)
        if update_type is None:
            raise NotImplementedError("unsupported update type: %s" % self)

        method_name: Optional[str] = None
        if method is not None:
            if not hasattr(method, "__name__"):
                raise Exception("unable to queue method: %s" % method)
            method_name = method.__name__

        partition_key, row_key = self.get_keys()

        queue_update(
            update_type,
            resolve(partition_key),
            resolve(row_key),
            method=method_name,
            visibility_timeout=visibility_timeout,
        )
