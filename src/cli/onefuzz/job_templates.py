#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.


import os
from typing import List

from onefuzztypes.job_templates import (
    JobTemplate,
    JobTemplateCreate,
    JobTemplateDelete,
    JobTemplateIndex,
    JobTemplateUpdate,
)
from onefuzztypes.responses import BoolResult

from .api import Endpoint, Onefuzz
from .backend import ONEFUZZ_BASE_PATH

TEMPLATE_CACHE = os.path.join(ONEFUZZ_BASE_PATH, "templates.json")


class Manage(Endpoint):
    endpoint = "job_templates/manage"

    def list(self) -> List[JobTemplateIndex]:
        self.onefuzz.logger.debug("listing job templates")
        return self._req_model_list("GET", JobTemplateIndex)

    def create(self, domain: str, name: str, template: JobTemplate) -> BoolResult:
        self.onefuzz.logger.debug("creating job templates")
        return self._req_model(
            "POST",
            BoolResult,
            data=JobTemplateCreate(domain=domain, name=name, template=template),
        )

    def update(self, domain: str, name: str, template: JobTemplate) -> BoolResult:
        self.onefuzz.logger.debug("update job templates")
        return self._req_model(
            "POST",
            BoolResult,
            data=JobTemplateUpdate(domain=domain, name=name, template=template),
        )

    def delete(self, domain: str, name: str) -> BoolResult:
        self.onefuzz.logger.debug("delete job templates")
        return self._req_model(
            "DELETE",
            BoolResult,
            data=JobTemplateDelete(domain=domain, name=name),
        )


class JobTemplates(Endpoint):
    """ Pre-defined job templates """

    def __init__(self, onefuzz: Onefuzz):
        super().__init__(onefuzz)
        self.manage = Manage(onefuzz)

    def refresh(self) -> None:
        pass
