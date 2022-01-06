#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.


import json

from cattr import unstructure
from functional import seq
from onefuzztypes.models import Report
from sarif_om import (
    Result,
    Run,
    SarifLog,
    Stack,
    StackFrame,
    Tool,
    ToolComponent,
)


def generate_sarif(report: Report) -> str:
    stack_frames = (
        seq(report.call_stack).map(lambda stack: StackFrame(location=stack)).to_list()
    )

    sarif_log = SarifLog(
        runs=[
            Run(
                tool=Tool(
                    driver=ToolComponent(
                        name="onefuzz",  # TODO: this might need to be more specific
                        semantic_version="0.0.1",  # TODO: get the onfuzz version
                        organization="Microsoft",
                        product="OneFuzz",
                        short_description="Onefuzz fuzzing platform",
                    ),
                    extensions="",
                    properties="",
                ),
                results=[
                    Result(
                        stacks=[Stack(frames=stack_frames)],
                        message=report.scariness_description,
                        locations=[],
                    )
                ],
            )
        ],
        version="2.1.0",
    )
    log = unstructure(sarif_log)

    return json.dumps(log, indent=4)
