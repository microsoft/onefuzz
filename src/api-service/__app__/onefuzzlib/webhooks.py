#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import datetime
import hmac
import json
import logging
from hashlib import sha512
from typing import List, Optional, Tuple
from uuid import UUID, uuid4

import requests
from memoization import cached
from onefuzztypes.enums import ErrorCode, WebhookMessageState
from onefuzztypes.events import Event, EventMessage, EventPing, EventType
from onefuzztypes.models import Error, Result
from onefuzztypes.webhooks import Webhook as BASE_WEBHOOK
from onefuzztypes.webhooks import (
    WebhookMessage,
    WebhookMessageEventGrid,
    WebhookMessageFormat,
)
from onefuzztypes.webhooks import WebhookMessageLog as BASE_WEBHOOK_MESSAGE_LOG
from pydantic import BaseModel, Field

from .__version__ import __version__
from .azure.creds import get_instance_id, get_instance_name
from .azure.queue import queue_object
from .azure.storage import StorageType
from .orm import MappingIntStrAny, ORMMixin

MAX_TRIES = 5
EXPIRE_DAYS = 7
USER_AGENT = "onefuzz-webhook %s" % (__version__)


class WebhookMessageQueueObj(BaseModel):
    webhook_id: UUID
    event_id: UUID


class WebhookMessageLog(BASE_WEBHOOK_MESSAGE_LOG, ORMMixin):
    timestamp: Optional[datetime.datetime] = Field(alias="Timestamp")

    @classmethod
    def key_fields(cls) -> Tuple[str, Optional[str]]:
        return ("webhook_id", "event_id")

    def export_exclude(self) -> Optional[MappingIntStrAny]:
        return {"etag": ..., "timestamp": ...}

    @classmethod
    def search_expired(cls) -> List["WebhookMessageLog"]:
        expire_time = datetime.datetime.utcnow() - datetime.timedelta(days=EXPIRE_DAYS)
        time_filter = "Timestamp lt datetime'%s'" % expire_time.isoformat()
        return cls.search(raw_unchecked_filter=time_filter)

    @classmethod
    def process_from_queue(cls, obj: WebhookMessageQueueObj) -> None:
        message = cls.get(obj.webhook_id, obj.event_id)
        if message is None:
            logging.error(
                "webhook message missing. %s:%s", obj.webhook_id, obj.event_id
            )
            return
        message.process()

    def process(self) -> None:
        if self.state in [WebhookMessageState.failed, WebhookMessageState.succeeded]:
            logging.info(
                "webhook message already handled: %s:%s", self.webhook_id, self.event_id
            )
            return

        self.try_count += 1

        logging.debug("sending webhook: %s:%s", self.webhook_id, self.event_id)
        if self.send():
            self.state = WebhookMessageState.succeeded
            self.save()
            logging.info("sent webhook event: %s:%s", self.webhook_id, self.event_id)
            return

        if self.try_count < MAX_TRIES:
            self.state = WebhookMessageState.retrying
            self.save()
            self.queue_webhook()
            logging.warning(
                "sending webhook event failed, re-queued. %s:%s",
                self.webhook_id,
                self.event_id,
            )
        else:
            self.state = WebhookMessageState.failed
            self.save()
            logging.warning(
                "sending webhook event failed %d times. %s:%s",
                self.try_count,
                self.webhook_id,
                self.event_id,
            )

    def send(self) -> bool:
        webhook = Webhook.get_by_id(self.webhook_id)
        if isinstance(webhook, Error):
            logging.error(
                "webhook no longer exists: %s:%s", self.webhook_id, self.event_id
            )
            return False

        try:
            return webhook.send(self)
        except Exception as err:
            logging.error(
                "webhook failed with exception: %s:%s - %s",
                self.webhook_id,
                self.event_id,
                err,
            )
            return False

    def queue_webhook(self) -> None:
        obj = WebhookMessageQueueObj(webhook_id=self.webhook_id, event_id=self.event_id)

        if self.state == WebhookMessageState.queued:
            visibility_timeout = 0
        elif self.state == WebhookMessageState.retrying:
            visibility_timeout = 30
        else:
            logging.error(
                "invalid WebhookMessage queue state, not queuing. %s:%s - %s",
                self.webhook_id,
                self.event_id,
                self.state,
            )
            return

        queue_object(
            "webhooks",
            obj,
            StorageType.config,
            visibility_timeout=visibility_timeout,
        )


class Webhook(BASE_WEBHOOK, ORMMixin):
    @classmethod
    def key_fields(cls) -> Tuple[str, Optional[str]]:
        return ("webhook_id", "name")

    @classmethod
    def send_event(cls, event_message: EventMessage) -> None:
        for webhook in get_webhooks_cached():
            if event_message.event_type not in webhook.event_types:
                continue

            webhook._add_event(event_message)

    @classmethod
    def get_by_id(cls, webhook_id: UUID) -> Result["Webhook"]:
        webhooks = cls.search(query={"webhook_id": [webhook_id]})
        if not webhooks:
            return Error(
                code=ErrorCode.INVALID_REQUEST, errors=["unable to find webhook"]
            )

        if len(webhooks) != 1:
            return Error(
                code=ErrorCode.INVALID_REQUEST,
                errors=["error identifying Notification"],
            )
        webhook = webhooks[0]
        return webhook

    def _add_event(self, event_message: EventMessage) -> None:
        message = WebhookMessageLog(
            webhook_id=self.webhook_id,
            event_id=event_message.event_id,
            event_type=event_message.event_type,
            event=event_message.event,
            instance_id=event_message.instance_id,
            instance_name=event_message.instance_name,
        )
        message.save()
        message.queue_webhook()

    def ping(self) -> EventPing:
        ping = EventPing(ping_id=uuid4())
        self._add_event(
            EventMessage(
                event_type=EventType.ping,
                event=ping,
                instance_id=get_instance_id(),
                instance_name=get_instance_name(),
            )
        )
        return ping

    def send(self, message_log: WebhookMessageLog) -> bool:
        if self.url is None:
            raise Exception("webhook URL incorrectly removed: %s" % self.webhook_id)

        data, digest = build_message(
            webhook_id=self.webhook_id,
            event_id=message_log.event_id,
            event_type=message_log.event_type,
            event=message_log.event,
            secret_token=self.secret_token,
            message_format=self.message_format,
        )

        headers = {"Content-type": "application/json", "User-Agent": USER_AGENT}

        if digest:
            headers["X-Onefuzz-Digest"] = digest

        response = requests.post(
            self.url,
            data=data,
            headers=headers,
        )
        return response.ok


def build_message(
    *,
    webhook_id: UUID,
    event_id: UUID,
    event_type: EventType,
    event: Event,
    secret_token: Optional[str] = None,
    message_format: Optional[WebhookMessageFormat] = None,
) -> Tuple[bytes, Optional[str]]:

    if message_format and message_format == WebhookMessageFormat.event_grid:
        decoded = [
            json.loads(
                WebhookMessageEventGrid(
                    id=event_id,
                    data=event,
                    dataVersion="1.0.0",
                    subject=get_instance_name(),
                    eventType=event_type,
                    eventTime=datetime.datetime.now(datetime.timezone.utc),
                ).json(sort_keys=True, exclude_none=True)
            )
        ]
        data = json.dumps(decoded).encode()
    else:
        data = (
            WebhookMessage(
                webhook_id=webhook_id,
                event_id=event_id,
                event_type=event_type,
                event=event,
                instance_id=get_instance_id(),
                instance_name=get_instance_name(),
            )
            .json(sort_keys=True, exclude_none=True)
            .encode()
        )
    digest = None
    if secret_token:
        digest = hmac.new(secret_token.encode(), msg=data, digestmod=sha512).hexdigest()
    return (data, digest)


@cached(ttl=30)
def get_webhooks_cached() -> List[Webhook]:
    return Webhook.search()
