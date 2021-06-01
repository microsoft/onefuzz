#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import datetime
import logging
from typing import List, Optional

from onefuzztypes.job_templates import JobTemplateConfig

from ..api import Endpoint, Onefuzz, PreviewFeature
from .builder import build_template_func
from .cache import CachedTemplates
from .handlers import TemplateSubmitHandler
from .manage import Manage

LOGGER = logging.getLogger("job-templates")


def load_templates(templates: List[JobTemplateConfig]) -> None:
    handlers = {
        TemplateSubmitHandler: build_template_func,
    }

    for handler in handlers:
        for name in dir(handler):
            if name.startswith("_"):
                continue
            delattr(handler, name)

        for template in templates:
            setattr(handler, template.name, handlers[handler](template))


class JobTemplates(Endpoint):
    """Job Templates"""

    endpoint = "job_templates"

    def __init__(self, onefuzz: Onefuzz):
        super().__init__(onefuzz)
        self.manage = Manage(onefuzz)
        self.submit = TemplateSubmitHandler(onefuzz)

    def info(self, name: str) -> Optional[JobTemplateConfig]:
        """Display information for a Job Template"""
        self.onefuzz._warn_preview(PreviewFeature.job_templates)

        endpoint = self.onefuzz._backend.config.endpoint
        if endpoint is None:
            return None

        entry = CachedTemplates.get(endpoint)
        if entry is None:
            return None

        for config in entry.configs:
            if config.name == name:
                return config

        return None

    def list(self) -> Optional[List[str]]:
        """List available Job Templates"""

        self.onefuzz._warn_preview(PreviewFeature.job_templates)

        endpoint = self.onefuzz._backend.config.endpoint
        if endpoint is None:
            return None

        entry = CachedTemplates.get(endpoint)
        if entry is None:
            return None

        return [x.name for x in entry.configs]

    def _load_cache(self) -> None:
        endpoint = self.onefuzz._backend.config.endpoint
        if endpoint is None:
            return

        yesterday = datetime.datetime.utcnow() - datetime.timedelta(hours=24)
        entry = CachedTemplates.get(endpoint)
        if not entry or entry.timestamp < yesterday:
            self.refresh()
            return

        load_templates(entry.configs)

    def refresh(self) -> None:
        """Update available templates"""
        self.onefuzz._warn_preview(PreviewFeature.job_templates)
        self.onefuzz.logger.info("refreshing job template cache")

        endpoint = self.onefuzz._backend.config.endpoint
        if endpoint is None:
            return None

        templates = self._req_model_list("GET", JobTemplateConfig)

        for template in templates:
            self.onefuzz.logger.info("updated template definition: %s", template.name)

        CachedTemplates.add(endpoint, templates)

        load_templates(templates)
