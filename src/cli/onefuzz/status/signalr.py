#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from typing import Any, Callable, Optional, cast

from signalrcore.hub_connection_builder import HubConnectionBuilder


class Stream:
    connected = None
    hub: Optional[HubConnectionBuilder] = None

    def __init__(self, onefuzz: Any, logger: logging.Logger) -> None:
        self.onefuzz = onefuzz
        self.logger = logger

    def get_token(self) -> str:
        negotiate = self.onefuzz._backend.request("POST", "negotiate")
        return cast(str, negotiate["accessToken"])

    def setup(self, handler: Callable) -> None:
        config = self.onefuzz._backend.request("POST", "negotiate")
        url = config["url"].replace("https://", "wss://")
        self.hub = (
            HubConnectionBuilder()
            .with_url(
                url,
                options={
                    "access_token_factory": self.get_token,
                    "skip_negotiation": True,
                },
            )
            .with_automatic_reconnect(
                {"type": "interval", "keep_alive_interval": 10, "reconnect_interval": 5}
            )
            .build()
        )
        self.hub.on_open(self.on_connect)
        self.hub.on_close(self.on_close)
        self.hub.on("dashboard", handler)
        self.logger.info("connecting to signalr")
        self.hub.start()

    def on_connect(self) -> None:
        self.logger.debug("connected")
        self.connected = True

    def on_close(self) -> None:
        self.logger.debug("disconnected")
        self.connected = False

    def stop(self) -> None:
        if self.hub:
            self.hub.stop()
