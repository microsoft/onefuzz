#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from typing import Optional, Tuple

from onefuzztypes.events import EventInstanceConfigUpdated
from onefuzztypes.models import InstanceConfig as BASE_CONFIG
from pydantic import Field

from .azure.creds import get_instance_name
from .events import send_event
from .orm import ORMMixin


class InstanceConfig(BASE_CONFIG, ORMMixin):
    instance_name: str = Field(default_factory=get_instance_name)

    @classmethod
    def key_fields(cls) -> Tuple[str, Optional[str]]:
        return ("instance_name", None)

    @classmethod
    def fetch(cls) -> "InstanceConfig":
        entry = cls.get(get_instance_name())
        if entry is None:
            entry = cls(allowed_aad_tenants=[])
            entry.save()
        return entry

    def save(self, new: bool = False, require_etag: bool = False) -> None:
        super().save(new=new, require_etag=require_etag)
        send_event(EventInstanceConfigUpdated(config=self))
