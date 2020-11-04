#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from typing import Optional

import azure.functions as func
from onefuzztypes.models import (
    Error,
    NodeEvent,
    NodeEventEnvelope,
    NodeStateUpdate,
    WorkerEvent,
)
from onefuzztypes.responses import BoolResult

from ..onefuzzlib.agent_authorization import verify_token
from ..onefuzzlib.agent_events import on_state_update, on_worker_event
from ..onefuzzlib.request import not_ok, ok, parse_request


def process(envelope: NodeEventEnvelope) -> Optional[Error]:
    logging.info(
        "node event: machine_id: %s event: %s",
        envelope.machine_id,
        envelope.event,
    )

    if isinstance(envelope.event, NodeStateUpdate):
        return on_state_update(envelope.machine_id, envelope.event)

    if isinstance(envelope.event, WorkerEvent):
        return on_worker_event(envelope.machine_id, envelope.event)

    if isinstance(envelope.event, NodeEvent):
        if envelope.event.state_update:
            result = on_state_update(envelope.machine_id, envelope.event.state_update)
            if result is not None:
                return result

        if envelope.event.worker_event:
            result = on_worker_event(envelope.machine_id, envelope.event.worker_event)
            if result is not None:
                return result

        return None

    raise NotImplementedError("invalid node event: %s" % envelope)


def post(req: func.HttpRequest) -> func.HttpResponse:
    envelope = parse_request(NodeEventEnvelope, req)
    if isinstance(envelope, Error):
        return not_ok(envelope, context="node event")

    result = process(envelope)
    if isinstance(result, Error):
        return not_ok(result, context="node event")

    return ok(BoolResult(result=True))


def main(req: func.HttpRequest) -> func.HttpResponse:
    return verify_token(req, post)
