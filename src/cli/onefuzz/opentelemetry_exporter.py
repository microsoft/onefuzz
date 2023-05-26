import typing
import logging
import os

from opentelemetry.sdk.trace.export import SpanExporter, SpanExportResult
from opentelemetry.sdk.trace import ReadableSpan

LOGGER = logging.getLogger("opentelemetry")


class OneFuzzSpanExporter(SpanExporter):
    def __init__(
        self,
        formatter: typing.Callable[
            [ReadableSpan], str
        ] = lambda span: span.to_json()
        + os.linesep,
    ):
        self.formatter = formatter

    def export(self, spans: typing.Sequence[ReadableSpan]) -> SpanExportResult:
        for span in spans:
            LOGGER.debug(self.formatter(span))
        return SpanExportResult.SUCCESS

    def force_flush(self, timeout_millis: int = 30000) -> bool:
        return True
