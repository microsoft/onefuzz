#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
import time
from queue import PriorityQueue
from threading import Thread
from typing import Any, Optional

from .cache import JobFilter, TopCache
from .signalr import Stream
from .top_view import render


def background_task(queue: PriorityQueue) -> None:
    while True:
        (_, entry) = queue.get(block=True)
        if entry is None:
            queue.task_done()
            return

        (cmd, args) = entry
        cmd(*args)
        queue.task_done()


class Top:
    def __init__(
        self,
        onefuzz: "Onefuzz",
        logger: logging.Logger,
        job_filter: JobFilter,
    ):
        self.onefuzz = onefuzz
        self.logger = logger

        self.cache = TopCache(onefuzz, job_filter)
        self.queue: PriorityQueue = PriorityQueue()
        self.worker = Thread(target=background_task, args=(self.queue,))
        self.worker.start()

    def add_container(self, name: str) -> None:
        if name in self.cache.files:
            return
        self.queue.put((2, (self.cache.add_container, [name])))

    def handler(self, message: Any) -> None:
        for event_raw in message:
            self.cache.add_message(event_raw)

    def setup(self) -> Stream:
        client = Stream(self.onefuzz, self.logger)
        client.setup(self.handler)

        self.logger.info("getting initial data")

        pools = self.onefuzz.pools.list()
        for pool in pools:
            self.cache.add_pool(pool)

        for job in self.onefuzz.jobs.list():
            mini_job = self.cache.add_job(job)
            # don't add pre-add tasks that we're going to filter out
            if not self.cache.should_render_job(mini_job):
                continue

            for task in self.onefuzz.tasks.list(job_id=job.job_id):
                self.cache.add_task(task)
                for container in task.config.containers:
                    self.add_container(container.name)

        nodes = self.onefuzz.nodes.list()
        for node in nodes:
            self.cache.add_node(node)

        if client.connected is None:
            self.logger.info("waiting for signalr connection")
            while client.connected is None:
                time.sleep(1)

        return client

    def run(self) -> None:
        try:
            client = self.setup()
        except Exception as err:
            self.queue.put((1, None))
            raise err

        error: Optional[Exception] = None
        try:
            self.logger.info("rendering")
            render(self.cache)
            client.stop()
        except Exception as err:
            error = err
        self.queue.put((1, None))
        if error is not None:
            raise error


from ..api import Onefuzz  # noqa: E402
