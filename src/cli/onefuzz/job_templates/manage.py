#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from typing import List

from onefuzztypes.job_templates import (
    JobTemplate,
    JobTemplateCreate,
    JobTemplateDelete,
    JobTemplateIndex,
    JobTemplateUpdate,
)
from onefuzztypes.responses import BoolResult

from ..api import Endpoint, PreviewFeature


class Manage(Endpoint):
    """ Manage Job Templates """

    endpoint = "job_templates/manage"

    def list(self) -> List[JobTemplateIndex]:
        """ List templates """
        self.onefuzz._warn_preview(PreviewFeature.job_templates)

        self.onefuzz.logger.debug("listing job templates")
        return self._req_model_list("GET", JobTemplateIndex)

    def create(self, domain: str, name: str, template: JobTemplate) -> BoolResult:
        """ Create a Job Template """
        self.onefuzz._warn_preview(PreviewFeature.job_templates)

        self.onefuzz.logger.debug("creating job templates")
        return self._req_model(
            "POST",
            BoolResult,
            data=JobTemplateCreate(domain=domain, name=name, template=template),
        )

    def update(self, domain: str, name: str, template: JobTemplate) -> BoolResult:
        """ Update an existing Job Template """
        self.onefuzz._warn_preview(PreviewFeature.job_templates)

        self.onefuzz.logger.debug("update job templates")
        return self._req_model(
            "POST",
            BoolResult,
            data=JobTemplateUpdate(domain=domain, name=name, template=template),
        )

    def delete(self, domain: str, name: str) -> BoolResult:
        """ Delete a Job Template """
        self.onefuzz._warn_preview(PreviewFeature.job_templates)

        self.onefuzz.logger.debug("delete job templates")
        return self._req_model(
            "DELETE",
            BoolResult,
            data=JobTemplateDelete(domain=domain, name=name),
        )
