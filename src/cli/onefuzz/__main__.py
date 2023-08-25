# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

"""
Command line interface to the Onefuzz service
"""

import sys

from opentelemetry import trace
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import SimpleSpanProcessor

from onefuzz.__version__ import __version__
from onefuzz.api import Command, Endpoint, Onefuzz
from onefuzz.cli import execute_api
from onefuzz.opentelemetry_exporter import OneFuzzSpanExporter

provider = TracerProvider()
processor = SimpleSpanProcessor(OneFuzzSpanExporter())
provider.add_span_processor(processor)
trace.set_tracer_provider(provider)


tracer = trace.get_tracer(__name__)


def main() -> int:
    with tracer.start_as_current_span("cli"):
        return execute_api(Onefuzz(), [Endpoint, Command], __version__)


if __name__ == "__main__":
    sys.exit(main())
