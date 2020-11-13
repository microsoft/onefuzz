#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from typing import List, Optional, Tuple

from onefuzztypes.consts import BUILTIN_TEMPLATE_DOMAIN
from onefuzztypes.job_templates import JobTemplateIndex as BASE_INDEX

from ..orm import ORMMixin
from .default_templates import TEMPLATES


class JobTemplateIndex(BASE_INDEX, ORMMixin):
    @classmethod
    def key_fields(cls) -> Tuple[str, Optional[str]]:
        return ("domain", "name")

    @classmethod
    def list(cls) -> List[BASE_INDEX]:
        # note, this returns the underlying BaseModel version, to prevent
        # mistakenly saving any of our default templates to Azure Table

        entries = [
            BASE_INDEX(name=name, domain=BUILTIN_TEMPLATE_DOMAIN, template=template)
            for (name, template) in TEMPLATES.items()
        ]

        entries += [
            BASE_INDEX(name=x.name, domain=x.domain, template=x.template)
            for x in cls.search()
        ]

        return entries
