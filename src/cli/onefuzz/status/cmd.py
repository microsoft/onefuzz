#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from typing import List, Optional
from uuid import UUID

from ..api import Command
from .cache import JobFilter
from .raw import raw
from .top import Top


class Status(Command):
    """ Monitor status of Onefuzz Instance """

    def raw(self) -> None:
        """ Raw status update stream """
        raw(self.onefuzz, self.logger)

    def top(
        self,
        *,
        show_details: bool = False,
        job_id: Optional[List[UUID]] = None,
        project: Optional[List[str]] = None,
        name: Optional[List[str]] = None,
    ) -> None:
        """ Onefuzz Top """
        job_filter = JobFilter(job_id=job_id, project=project, name=name)
        top = Top(self.onefuzz, self.logger, show_details, job_filter)
        top.run()
