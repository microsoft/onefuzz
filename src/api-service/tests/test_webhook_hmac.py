#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import unittest
from unittest.mock import MagicMock, patch
from uuid import UUID

from onefuzztypes.events import EventPing, EventType


class TestWebhookHmac(unittest.TestCase):
    @patch("__app__.onefuzzlib.webhooks.get_instance_id")
    @patch("__app__.onefuzzlib.webhooks.get_instance_name")
    def test_webhook_hmac(self, mock_name: MagicMock, mock_id: MagicMock) -> None:
        mock_name.return_value = "example"
        mock_id.return_value = UUID(int=3)

        # late import to enable the patch to function
        from __app__.onefuzzlib.webhooks import build_message

        webhook_id = UUID(int=0)
        event_id = UUID(int=1)
        event_type = EventType.ping
        event = EventPing(ping_id=UUID(int=2))

        data, digest = build_message(
            webhook_id=webhook_id,
            event_id=event_id,
            event_type=event_type,
            event=event,
        )

        expected = (
            b"{"
            b'"event": {"ping_id": "00000000-0000-0000-0000-000000000002"}, '
            b'"event_id": "00000000-0000-0000-0000-000000000001", '
            b'"event_type": "ping", '
            b'"instance_id": "00000000-0000-0000-0000-000000000003", '
            b'"instance_name": "example", '
            b'"webhook_id": "00000000-0000-0000-0000-000000000000"'
            b"}"
        )

        expected_digest = (
            "2f0610d708d9938dc053200cd2242b4cca4e9bb7227e5e662f28307f57c7fc9b"
            "d1ffcab6fff9f409b3fb856db4f358be9078ec0b64874efac2b8f065211e2a14"
        )

        print(repr(expected))
        print(repr(data))
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
