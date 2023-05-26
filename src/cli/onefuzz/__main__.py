# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

"""
Command line interface to the Onefuzz service
"""

import sys

from onefuzz.__version__ import __version__
from onefuzz.api import Command, Endpoint, Onefuzz
from onefuzz.cli import execute_api

from opentelemetry import trace
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import (
    ConsoleSpanExporter,
    SimpleSpanProcessor
)

trace.set_tracer_provider(TracerProvider())
trace.get_tracer_provider().add_span_processor(
    SimpleSpanProcessor(ConsoleSpanExporter())
)

tracer = trace.get_tracer(__name__)


def main() -> int:
    with tracer.start_as_current_span("cli"):
        return execute_api(Onefuzz(), [Endpoint, Command], __version__)


if __name__ == "__main__":
    sys.exit(main())
