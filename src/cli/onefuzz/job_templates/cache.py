#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import datetime
import json
import os
from typing import Dict, List, Optional

from onefuzztypes.job_templates import JobTemplateConfig
from pydantic import BaseModel, Field

from ..backend import ONEFUZZ_BASE_PATH

TEMPLATE_CACHE = os.path.expanduser(os.path.join(ONEFUZZ_BASE_PATH, "templates.json"))


class EndpointCache(BaseModel):
    timestamp: datetime.datetime
    configs: List[JobTemplateConfig]


class CachedTemplates(BaseModel):
    entries: Dict[str, EndpointCache] = Field(default_factory=dict)

    @classmethod
    def add(cls, endpoint: str, configs: List[JobTemplateConfig]) -> None:
        cache = cls.load()
        cache.entries[endpoint] = EndpointCache(
            timestamp=datetime.datetime.utcnow(), configs=configs
        )
        cache.save()

    @classmethod
    def get(cls, endpoint: str) -> Optional[EndpointCache]:
        cache = cls.load()
        return cache.entries.get(endpoint)

    @classmethod
    def load(cls) -> "CachedTemplates":
        if not os.path.exists(TEMPLATE_CACHE):
            entry = cls()
            entry.save()
            return entry

        with open(TEMPLATE_CACHE, "r") as handle:
            raw = json.load(handle)

        return cls.parse_obj(raw)

    def save(self) -> None:
        with open(TEMPLATE_CACHE, "w") as handle:
            handle.write(self.json())
