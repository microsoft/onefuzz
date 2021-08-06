#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
import logging
from typing import Dict

import azure.functions as func

# Declare OpenTelemetry as enabled tracing plugin for Azure SDKs
from azure.core.settings import settings
from azure.core.tracing.ext.opentelemetry_span import OpenTelemetrySpan
from opentelemetry import trace
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor

from ..onefuzzlib.azure.storage import corpus_accounts
from ..onefuzzlib.notifications.main import new_files
from ..onefuzzlib.otelemetry import get_otel_client

settings.tracing_implementation = OpenTelemetrySpan

trace.set_tracer_provider(TracerProvider())
tracer = trace.get_tracer(__name__)
span_processor = BatchSpanProcessor(get_otel_client())
trace.get_tracer_provider().add_span_processor(span_processor)

# The number of time the function will be retried if an error occurs
# https://docs.microsoft.com/en-us/azure/azure-functions/functions-bindings-storage-queue-trigger?tabs=csharp#poison-messages
MAX_DEQUEUE_COUNT = 5


def file_added(event: Dict, fail_task_on_transient_error: bool) -> None:
    parts = event["data"]["url"].split("/")[3:]
    container = parts[0]
    path = "/".join(parts[1:])
    logging.info("file added container: %s - path: %s", container, path)
    print("[otel]file added container: %s - path: %s", container, path)
    new_files(container, path, fail_task_on_transient_error)


def main(msg: func.QueueMessage) -> None:
    with tracer.start_as_current_span("[otel] queue file changes root span"):
        event = json.loads(msg.get_body())
        last_try = msg.dequeue_count == MAX_DEQUEUE_COUNT
        # check type first before calling Azure APIs
        if event["eventType"] != "Microsoft.Storage.BlobCreated":
            return

        if event["topic"] not in corpus_accounts():
            return

        with tracer.start_as_current_span("[otel] adding files"):
            file_added(event, last_try)
