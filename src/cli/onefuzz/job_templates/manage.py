#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from typing import List

from onefuzztypes.job_templates import (
    JobTemplate,
    JobTemplateDelete,
    JobTemplateGet,
    JobTemplateIndex,
    JobTemplateSave,
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

    def get(self, name: str) -> JobTemplate:
        """ Get an existing Job Template """
        self.onefuzz._warn_preview(PreviewFeature.job_templates)

        self.onefuzz.logger.debug("get job template")
        return self._req_model("GET", JobTemplate, data=JobTemplateGet(name=name))

    def save(self, name: str, template: JobTemplate) -> BoolResult:
        """ Save a Job Template """
        self.onefuzz._warn_preview(PreviewFeature.job_templates)

        self.onefuzz.logger.debug("creating job templates")
        return self._req_model(
            "POST",
            BoolResult,
            data=JobTemplateSave(name=name, template=template),
        )

    def delete(self, name: str) -> BoolResult:
        """ Delete a Job Template """
        self.onefuzz._warn_preview(PreviewFeature.job_templates)

        self.onefuzz.logger.debug("delete job templates")
        return self._req_model(
            "DELETE",
            BoolResult,
            data=JobTemplateDelete(name=name),
        )
