#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import unittest
from uuid import UUID

from onefuzztypes.enums import WebhookEventType
from onefuzztypes.webhooks import WebhookEventPing

from __app__.onefuzzlib.webhooks import build_message


class TestWebhookHmac(unittest.TestCase):
    def test_webhook_hmac(self) -> None:
        webhook_id = UUID(int=0)
        event_id = UUID(int=1)
        event_type = WebhookEventType.ping
        event = WebhookEventPing(ping_id=UUID(int=2))

        data, digest = build_message(
            webhook_id=webhook_id, event_id=event_id, event_type=event_type, event=event
        )

        expected = (
            b"{"
            b'"event": {"ping_id": "00000000-0000-0000-0000-000000000002"}, '
            b'"event_id": "00000000-0000-0000-0000-000000000001", '
            b'"event_type": "ping", '
            b'"webhook_id": "00000000-0000-0000-0000-000000000000"'
            b"}"
        )

        expected_digest = (
            "3502f83237ce006b7f6cfa40b89c0295009e3ccb0a1e62ce1d689700c2c6e698"
            "61c0de81e011495c2ca89fbf99485b841cee257bcfba326a3edc66f39dc1feec"
        )

        print(repr(expected))
        self.assertEqual(data, expected)
        self.assertEqual(digest, None)

        data, digest = build_message(
            webhook_id=webhook_id,
            event_id=event_id,
            event_type=event_type,
            event=event,
            secret_token="hello there",
        )
        self.assertEqual(data, expected)
        self.assertEqual(digest, expected_digest)
