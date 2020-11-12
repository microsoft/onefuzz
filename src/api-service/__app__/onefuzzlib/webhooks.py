import datetime
import logging
from typing import List, Optional, Tuple
from uuid import UUID

import requests
from memoization import cached
from onefuzztypes.enums import ErrorCode, WebhookEventType, WebhookMessageState
from onefuzztypes.models import Error, Result
from onefuzztypes.webhooks import Webhook as BASE_WEBHOOK
from onefuzztypes.webhooks import WebhookEvent, WebhookEventPing, WebhookMessage
from onefuzztypes.webhooks import WebhookMessageLog as BASE_WEBHOOK_MESSAGE_LOG
from pydantic import BaseModel

from .__version__ import __version__

from .azure.creds import get_func_storage
from .azure.queue import queue_object
from .orm import ORMMixin

MAX_TRIES = 5
EXPIRE_DAYS = 7
USER_AGENT = "onefuzz-webhook %s" % (__version__)


class WebhookMessageQueueObj(BaseModel):
    webhook_id: UUID
    event_id: UUID


class WebhookMessageLog(BASE_WEBHOOK_MESSAGE_LOG, ORMMixin):
    @classmethod
    def key_fields(cls) -> Tuple[str, Optional[str]]:
        return ("webhook_id", "event_id")

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
        if not webhook:
            logging.error(
                "webhook no longer exists: %s:%s", self.webhook_id, self.event_id
            )
            return False

        data = WebhookMessage(
            webhook_id=self.webhook_id,
            event_id=self.event_id,
            event_type=self.event_type,
            event=self.event,
        ).json()

        if webhook.url is None:
            raise Exception("webhook URL incorrectly removed: %s", webhook.webhook_id)

        try:
            response = requests.post(
                webhook.url,
                data=data,
                headers={
                    "Content-type": "application/json",
                    "User-Agent": USER_AGENT,
                },
            )
            return response.ok
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
            visibility_timeout=visibility_timeout,
            account_id=get_func_storage(),
        )


class Webhook(BASE_WEBHOOK, ORMMixin):
    @classmethod
    def key_fields(cls) -> Tuple[str, Optional[str]]:
        return ("webhook_id", "name")

    @classmethod
    def send_event(cls, event_type: WebhookEventType, event: WebhookEvent) -> None:
        for webhook in get_webhooks_cached():
            if event_type not in webhook.event_types:
                continue

            webhook._add_event(event_type, event)

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

    def _add_event(self, event_type: WebhookEventType, event: WebhookEvent) -> None:
        message = WebhookMessageLog(
            webhook_id=self.webhook_id,
            event_type=event_type,
            event=event,
        )
        message.save()
        message.queue_webhook()

    def ping(self) -> WebhookEventPing:
        ping = WebhookEventPing()
        self._add_event(WebhookEventType.ping, ping)
        return ping


@cached(ttl=30)
def get_webhooks_cached() -> List[Webhook]:
    return Webhook.search()
