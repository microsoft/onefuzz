#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging

import azure.functions as func
from onefuzztypes.enums import JobState, NodeState, PoolState, TaskState, VmState

from ..onefuzzlib.dashboard import get_event
from ..onefuzzlib.jobs import Job
from ..onefuzzlib.orm import process_update
from ..onefuzzlib.pools import Node, Pool
from ..onefuzzlib.proxy import Proxy
from ..onefuzzlib.repro import Repro
from ..onefuzzlib.tasks.main import Task


def main(mytimer: func.TimerRequest, dashboard: func.Out[str]) -> None:  # noqa: F841
    proxies = Proxy.search_states(states=VmState.needs_work())
    for proxy in proxies:
        logging.info("update proxy vm: %s", proxy.region)
        process_update(proxy)

    vms = Repro.search_states(states=VmState.needs_work())
    for vm in vms:
        logging.info("update vm: %s", vm.vm_id)
        process_update(vm)

    tasks = Task.search_states(states=TaskState.needs_work())
    for task in tasks:
        logging.info("update task: %s", task.task_id)
        process_update(task)

    jobs = Job.search_states(states=JobState.needs_work())
    for job in jobs:
        logging.info("update job: %s", job.job_id)
        process_update(job)

    pools = Pool.search_states(states=PoolState.needs_work())
    for pool in pools:
        logging.info("update pool: %s (%s)", pool.pool_id, pool.name)
        process_update(pool)

    nodes = Node.search_states(states=NodeState.needs_work())
    for node in nodes:
        logging.info("update node: %s", node.machine_id)
        process_update(node)

    expired_tasks = Task.search_expired()
    for task in expired_tasks:
        logging.info("stopping task: %s", task.job_id)
        task.stopping()

    expired_jobs = Job.search_expired()
    for job in expired_jobs:
        logging.info("stopping job: %s", job.job_id)
        job.stopping()

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
