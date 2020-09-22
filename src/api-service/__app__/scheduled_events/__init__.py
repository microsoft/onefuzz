#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging

import azure.functions as func
from onefuzztypes.enums import (
    JobState,
    NodeState,
    PoolState,
    ScalesetState,
    TaskState,
    VmState,
)

from ..onefuzzlib.dashboard import get_event
from ..onefuzzlib.jobs import Job
from ..onefuzzlib.pools import Node, Pool, Scaleset
from ..onefuzzlib.proxy import Proxy
from ..onefuzzlib.repro import Repro
from ..onefuzzlib.tasks.main import Task


def main(mytimer: func.TimerRequest, dashboard: func.Out[str]) -> None:  # noqa: F841
    proxies = Proxy.search_states(states=VmState.needs_work())
    for proxy in proxies:
        logging.info("requeueing update proxy vm: %s", proxy.region)
        proxy.queue()

    vms = Repro.search_states(states=VmState.needs_work())
    for vm in vms:
        logging.info("requeueing update vm: %s", vm.vm_id)
        vm.queue()

    tasks = Task.search_states(states=TaskState.needs_work())
    for task in tasks:
        logging.info("requeueing update task: %s", task.task_id)
        task.queue()

    jobs = Job.search_states(states=JobState.needs_work())
    for job in jobs:
        logging.info("requeueing update job: %s", job.job_id)
        job.queue()

    pools = Pool.search_states(states=PoolState.needs_work())
    for pool in pools:
        logging.info("queuing update pool: %s (%s)", pool.pool_id, pool.name)
        pool.queue()

    nodes = Node.search_states(states=NodeState.needs_work())
    for node in nodes:
        logging.info("queuing update node: %s", node.machine_id)
        node.queue()

    expired_tasks = Task.search_expired()
    for task in expired_tasks:
        logging.info("queuing stop for task: %s", task.job_id)
        task.queue_stop()

    expired_jobs = Job.search_expired()
    for job in expired_jobs:
        logging.info("queuing stop for job: %s", job.job_id)
        job.queue_stop()

    # Reminder, proxies are created on-demand.  If something is "wrong" with
    # a proxy, the plan is: delete and recreate it.
    for proxy in Proxy.search():
        if not proxy.is_alive():
            logging.error("proxy alive check failed, stopping: %s", proxy.region)
            proxy.state = VmState.stopping
            proxy.save()
        else:
            proxy.save_proxy_config()

    event = get_event()
    if event:
        dashboard.set(event)
