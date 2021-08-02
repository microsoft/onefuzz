#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging

import azure.functions as func
from onefuzztypes.enums import VmState

from ..onefuzzlib.orm import process_state_updates
from ..onefuzzlib.repro import Repro


def main(mytimer: func.TimerRequest) -> None:  # noqa: F841
    expired = Repro.search_expired()
    for repro in expired:
        logging.info("stopping repro: %s", repro.vm_id)
        repro.stopping()

    expired_vm_ids = [x.vm_id for x in expired]

    for repro in Repro.search_states(states=VmState.needs_work()):
        if repro.vm_id in expired_vm_ids:
            # this VM already got processed during the expired phase
            continue
        logging.info("update repro: %s", repro.vm_id)
        process_state_updates(repro)
