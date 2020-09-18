#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
import os
from typing import Any, Dict, Optional, Union

from onefuzztypes.enums import TelemetryData, TelemetryEvent
from opencensus.ext.azure.log_exporter import AzureLogHandler

LOCAL_CLIENT: Optional[logging.Logger] = None
CENTRAL_CLIENT: Optional[logging.Logger] = None


def _get_client(environ_key: str) -> Optional[logging.Logger]:
    key = os.environ.get(environ_key)
    if key is None:
        return None
    client = logging.getLogger("onefuzz")
    client.addHandler(AzureLogHandler(connection_string="InstrumentationKey=%s" % key))
    return client


def _central_client() -> Optional[logging.Logger]:
    global CENTRAL_CLIENT
    if not CENTRAL_CLIENT:
        CENTRAL_CLIENT = _get_client("ONEFUZZ_TELEMETRY")
    return CENTRAL_CLIENT


def _local_client() -> Union[None, Any, logging.Logger]:
    global LOCAL_CLIENT
    if not LOCAL_CLIENT:
        LOCAL_CLIENT = _get_client("APPINSIGHTS_INSTRUMENTATIONKEY")
    return LOCAL_CLIENT


# NOTE: All telemetry that is *NOT* using the ORM telemetry_include should
# go through this method
#
# This provides a point of inspection to know if it's data that is safe to
# log to the central OneFuzz telemetry point
def track_event(
    event: TelemetryEvent, data: Dict[TelemetryData, Union[str, int]]
) -> None:
    central = _central_client()
    local = _local_client()

    if local:
        serialized = {k.name: v for (k, v) in data.items()}
        local.info(event.name, extra={"custom_dimensions": serialized})

    if event in TelemetryEvent.can_share() and central:
        serialized = {
            k.name: v for (k, v) in data.items() if k in TelemetryData.can_share()
        }
        central.info(event.name, extra={"custom_dimensions": serialized})


# NOTE: This should *only* be used for logging Telemetry data that uses
# the ORM telemetry_include method to limit data for telemetry.
def track_event_filtered(event: TelemetryEvent, data: Any) -> None:
    central = _central_client()
    local = _local_client()

    if local:
        local.info(event.name, extra={"custom_dimensions": data})

    if central and event in TelemetryEvent.can_share():
        central.info(event.name, extra={"custom_dimensions": data})
