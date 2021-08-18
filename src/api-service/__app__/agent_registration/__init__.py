#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import datetime
import logging
from uuid import UUID

import azure.functions as func
from onefuzztypes.enums import ErrorCode
from onefuzztypes.models import Error
from onefuzztypes.requests import AgentRegistrationGet, AgentRegistrationPost
from onefuzztypes.responses import AgentRegistration

from ..onefuzzlib.azure.creds import get_instance_url
from ..onefuzzlib.azure.queue import get_queue_sas
from ..onefuzzlib.azure.storage import StorageType
from ..onefuzzlib.endpoint_authorization import call_if_agent
from ..onefuzzlib.request import not_ok, ok, parse_uri
from ..onefuzzlib.workers.nodes import Node
from ..onefuzzlib.workers.pools import Pool


def create_registration_response(machine_id: UUID, pool: Pool) -> func.HttpResponse:
    base_address = get_instance_url()
    events_url = "%s/api/agents/events" % base_address
    commands_url = "%s/api/agents/commands" % base_address
    work_queue = get_queue_sas(
        pool.get_pool_queue(),
        StorageType.corpus,
        read=True,
        update=True,
        process=True,
        duration=datetime.timedelta(hours=24),
    )
    return ok(
        AgentRegistration(
            events_url=events_url,
            commands_url=commands_url,
            work_queue=work_queue,
        )
    )


def get(req: func.HttpRequest) -> func.HttpResponse:
    get_registration = parse_uri(AgentRegistrationGet, req)

    if isinstance(get_registration, Error):
        return not_ok(get_registration, context="agent registration")

    agent_node = Node.get_by_machine_id(get_registration.machine_id)

    if agent_node is None:
        return not_ok(
            Error(
                code=ErrorCode.INVALID_REQUEST,
                errors=[
                    "unable to find a registration associated with machine_id '%s'"
                    % get_registration.machine_id
                ],
            ),
            context="agent registration",
        )
    else:
        pool = Pool.get_by_name(agent_node.pool_name)
        if isinstance(pool, Error):
            return not_ok(
                Error(
                    code=ErrorCode.INVALID_REQUEST,
                    errors=[
                        "unable to find a pool associated with the provided machine_id"
                    ],
                ),
                context="agent registration",
            )

        return create_registration_response(agent_node.machine_id, pool)


def post(req: func.HttpRequest) -> func.HttpResponse:
    registration_request = parse_uri(AgentRegistrationPost, req)
    if isinstance(registration_request, Error):
        return not_ok(registration_request, context="agent registration")
    logging.info(
        "registration request: %s", registration_request.json(exclude_none=True)
    )

    pool = Pool.get_by_name(registration_request.pool_name)
    if isinstance(pool, Error):
        return not_ok(
            Error(
                code=ErrorCode.INVALID_REQUEST,
                errors=["unable to find pool '%s'" % registration_request.pool_name],
            ),
            context="agent registration",
        )

    node = Node.get_by_machine_id(registration_request.machine_id)
    if node:
        node.delete()

    node = Node.create(
        pool_id=pool.pool_id,
        pool_name=pool.name,
        machine_id=registration_request.machine_id,
        scaleset_id=registration_request.scaleset_id,
        version=registration_request.version,
    )

    return create_registration_response(node.machine_id, pool)


def main(req: func.HttpRequest) -> func.HttpResponse:
    methods = {"POST": post, "GET": get}
    method = methods[req.method]
    result = call_if_agent(req, method)

    return result
