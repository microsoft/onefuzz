#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging

import azure.functions as func
from onefuzztypes.enums import VmState

from ..onefuzzlib.dashboard import get_event
from ..onefuzzlib.orm import process_update
from ..onefuzzlib.repro import Repro


def main(mytimer: func.TimerRequest, dashboard: func.Out[str]) -> None:  # noqa: F841
    vms = Repro.search_states(states=VmState.needs_work())
    for vm in vms:
        logging.info("update vm: %s", vm.vm_id)
        process_update(vm)

    event = get_event()
    if event:
        dashboard.set(event)
