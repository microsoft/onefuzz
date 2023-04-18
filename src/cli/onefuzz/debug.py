#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
import logging
import os
import tempfile
import time
import uuid
from datetime import datetime
from typing import Any, Dict, List, Optional, Set, Tuple, Union
from urllib.parse import urlparse
from uuid import UUID

import jmespath
from azure.applicationinsights import ApplicationInsightsDataClient
from azure.applicationinsights.models import QueryBody
from azure.identity import AzureCliCredential
from azure.storage.blob import ContainerClient
from onefuzztypes import models, requests, responses
from onefuzztypes.enums import ContainerType, TaskType
from onefuzztypes.models import BlobRef, Job, NodeAssignment, Report, Task, TaskConfig
from onefuzztypes.primitives import Container, Directory, PoolName
from onefuzztypes.responses import TemplateValidationResponse

from onefuzz.api import UUID_EXPANSION, Command, Endpoint, Onefuzz

from .azure_identity_credential_adapter import AzureIdentityCredentialAdapter
from .backend import wait
from .rdp import rdp_connect
from .ssh import ssh_connect

EMPTY_SHA256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
ZERO_SHA256 = "0" * len(EMPTY_SHA256)
DAY_TIMESPAN = "PT24H"
HOUR_TIMESPAN = "PT1H"
DEFAULT_TAIL_DELAY = 10.0


class DebugRepro(Command):
    """Debug repro instances"""

    def _disambiguate(self, vm_id: UUID_EXPANSION) -> str:
        return str(
            self.onefuzz.repro._disambiguate_uuid(
                "vm_id",
                vm_id,
                lambda: [str(x.vm_id) for x in self.onefuzz.repro.list()],
            )
        )

    def _info(self) -> Tuple[str, str]:
        info = self.onefuzz.info.get()
        return info.resource_group, info.subscription

    def ssh(self, vm_id: str) -> None:
        vm_id = self._disambiguate(vm_id)
        repro = self.onefuzz.repro.get(vm_id)
        if repro.ip is None:
            raise Exception("missing IP: %s" % repro)
        if repro.auth is None:
            raise Exception("missing Auth: %s" % repro)

        with ssh_connect(repro.ip, repro.auth.private_key, call=True):
            pass

    def rdp(self, vm_id: str) -> None:
        vm_id = self._disambiguate(vm_id)
        repro = self.onefuzz.repro.get(vm_id)
        if repro.ip is None:
            raise Exception("missing IP: %s" % repro)
        if repro.auth is None:
            raise Exception("missing Auth: %s" % repro)

        RDP_PORT = 3389
        with rdp_connect(repro.ip, repro.auth.password, port=RDP_PORT):
            return


class DebugNode(Command):
    """Debug a specific node on a scaleset"""

    def rdp(self, machine_id: UUID_EXPANSION, duration: Optional[int] = 1) -> None:
        node = self.onefuzz.nodes.get(machine_id)
        if node.scaleset_id is None:
            raise Exception("node is not part of a scaleset")
        self.onefuzz.debug.scalesets.rdp(
            scaleset_id=node.scaleset_id, machine_id=node.machine_id, duration=duration
        )

    def ssh(self, machine_id: UUID_EXPANSION, duration: Optional[int] = 1) -> None:
        node = self.onefuzz.nodes.get(machine_id)
        if node.scaleset_id is None:
            raise Exception("node is not part of a scaleset")
        self.onefuzz.debug.scalesets.ssh(
            scaleset_id=node.scaleset_id, machine_id=node.machine_id, duration=duration
        )


class DebugScaleset(Command):
    """Debug tasks"""

    def _get_proxy_setup(
        self, scaleset_id: UUID, machine_id: UUID, port: int, duration: Optional[int]
    ) -> Tuple[bool, str, Optional[Tuple[str, int]]]:
        proxy = self.onefuzz.scaleset_proxy.create(
            scaleset_id, machine_id, port, duration=duration
        )
        if proxy.ip is None:
            return (False, "waiting on proxy ip", None)

        return (True, "waiting on proxy port", (proxy.ip, proxy.forward.src_port))

    def rdp(
        self,
        scaleset_id: UUID_EXPANSION,
        machine_id: UUID_EXPANSION,
        duration: Optional[int] = 1,
    ) -> None:
        (
            scaleset,
            machine_id_expanded,
        ) = self.onefuzz.scalesets._expand_scaleset_machine(
            scaleset_id, machine_id, include_auth=True
        )

        RDP_PORT = 3389
        setup = wait(
            lambda: self._get_proxy_setup(
                scaleset.scaleset_id, machine_id_expanded, RDP_PORT, duration
            )
        )
        if setup is None:
            raise Exception("no proxy for RDP port configured")

        if scaleset.auth is None:
            raise Exception("auth is not available for scaleset")

        ip, port = setup
        with rdp_connect(ip, scaleset.auth.password, port=port):
            return

    def ssh(
        self,
        scaleset_id: UUID_EXPANSION,
        machine_id: UUID_EXPANSION,
        duration: Optional[int] = 1,
        command: Optional[str] = None,
    ) -> None:
        (
            scaleset,
            machine_id_expanded,
        ) = self.onefuzz.scalesets._expand_scaleset_machine(
            scaleset_id, machine_id, include_auth=True
        )

        SSH_PORT = 22
        setup = wait(
            lambda: self._get_proxy_setup(
                scaleset.scaleset_id, machine_id_expanded, SSH_PORT, duration
            )
        )
        if setup is None:
            raise Exception("no proxy for SSH port configured")

        ip, port = setup

        if scaleset.auth is None:
            raise Exception("auth is not available for scaleset")

        with ssh_connect(
            ip, scaleset.auth.private_key, port=port, call=True, command=command
        ):
            return


class DebugTask(Command):
    """Debug a specific task"""

    def list_nodes(self, task_id: UUID_EXPANSION) -> Optional[List[NodeAssignment]]:
        task = self.onefuzz.tasks.get(task_id)
        return task.nodes

    def _get_node(
        self, task_id: UUID_EXPANSION, node_id: Optional[UUID]
    ) -> Tuple[UUID, UUID]:
        nodes = self.list_nodes(task_id)
        if not nodes:
            raise Exception("task is not currently executing on nodes")

        if node_id is not None:
            for node in nodes:
                if node.node_id == node_id and node.scaleset_id is not None:
                    return (node.scaleset_id, node.node_id)
            raise Exception("unable to find scaleset with node_id")

        for node in nodes:
            if node.scaleset_id:
                return (node.scaleset_id, node.node_id)

        raise Exception("unable to find scaleset node running on task")

    def ssh(
        self,
        task_id: UUID_EXPANSION,
        *,
        node_id: Optional[UUID] = None,
        duration: Optional[int] = 1,
    ) -> None:
        scaleset_id, node_id = self._get_node(task_id, node_id)
        return self.onefuzz.debug.scalesets.ssh(scaleset_id, node_id, duration=duration)

    def rdp(
        self,
        task_id: UUID_EXPANSION,
        *,
        node_id: Optional[UUID] = None,
        duration: Optional[int] = 1,
    ) -> None:
        scaleset_id, node_id = self._get_node(task_id, node_id)
        return self.onefuzz.debug.scalesets.rdp(scaleset_id, node_id, duration=duration)

    def libfuzzer_coverage(
        self,
        task_id: UUID_EXPANSION,
        timespan: str = DAY_TIMESPAN,
        limit: Optional[int] = None,
    ) -> Any:
        """
        Get the coverage for the specified task

        :param task_id value: Task ID
        :param str timespan: ISO 8601 duration format
        :param int limit: Limit the number of records returned
        """
        task = self.onefuzz.tasks.get(task_id)
        query = f"where customDimensions.task_id == '{task.task_id}'"
        return self.onefuzz.debug.logs._query_libfuzzer_coverage(query, timespan, limit)

    def libfuzzer_execs_sec(
        self,
        task_id: UUID_EXPANSION,
        timespan: str = DAY_TIMESPAN,
        limit: Optional[int] = None,
    ) -> Any:
        """
        Get the executions per second for the specified task

        :param task_id value: Task ID
        :param str timespan: ISO 8601 duration format
        :param int limit: Limit the number of records returned
        """
        task = self.onefuzz.tasks.get(task_id)
        query = f"where customDimensions.task_id == '{task.task_id}'"
        return self.onefuzz.debug.logs._query_libfuzzer_execs_sec(
            query, timespan, limit
        )

    def download_files(self, task_id: UUID_EXPANSION, output: Directory) -> None:
        """Download the containers by container type for the specified task"""

        self.onefuzz.containers.download_task(task_id, output=output)


class DebugJobTask(Command):
    """Debug a task for a specific job"""

    def _get_task(self, job_id: UUID_EXPANSION, task_type: TaskType) -> UUID:
        for task in self.onefuzz.tasks.list(job_id=job_id):
            if task.config.task.type == task_type:
                return task.task_id

        raise Exception(
            "unable to find task type %s for job:%s" % (task_type.name, job_id)
        )

    def ssh(
        self,
        job_id: UUID_EXPANSION,
        task_type: TaskType,
        *,
        duration: Optional[int] = 1,
    ) -> None:
        """SSH into the first node running the specified task type in the job"""
        return self.onefuzz.debug.task.ssh(
            self._get_task(job_id, task_type), duration=duration
        )

    def rdp(
        self,
        job_id: UUID_EXPANSION,
        task_type: TaskType,
        *,
        duration: Optional[int] = 1,
    ) -> None:
        """RDP into the first node running the specified task type in the job"""
        return self.onefuzz.debug.task.rdp(
            self._get_task(job_id, task_type), duration=duration
        )


class DebugJob(Command):
    """Debug a specific Job"""

    def __init__(self, onefuzz: Any, logger: logging.Logger):
        super().__init__(onefuzz, logger)
        self.task = DebugJobTask(onefuzz, logger)

    def libfuzzer_coverage(
        self,
        job_id: UUID_EXPANSION,
        timespan: str = DAY_TIMESPAN,
        limit: Optional[int] = None,
    ) -> Any:
        """
        Get the coverage for the specified job

        :param job_id value: Job ID
        :param str timespan: ISO 8601 duration format
        :param int limit: Limit the number of records returned
        """
        job = self.onefuzz.jobs.get(job_id)
        query = f"where customDimensions.job_id == '{job.job_id}'"
        return self.onefuzz.debug.logs._query_libfuzzer_coverage(query, timespan, limit)

    def libfuzzer_execs_sec(
        self,
        job_id: UUID_EXPANSION,
        timespan: str = DAY_TIMESPAN,
        limit: Optional[int] = None,
    ) -> Any:
        """
        Get the executions per second for the specified job

        :param job_id value: Job ID
        :param str timespan: ISO 8601 duration format
        :param int limit: Limit the number of records returned
        """
        job = self.onefuzz.jobs.get(job_id)
        query = f"where customDimensions.job_id == '{job.job_id}'"
        return self.onefuzz.debug.logs._query_libfuzzer_execs_sec(
            query, timespan, limit
        )

    def download_files(self, job_id: UUID_EXPANSION, output: Directory) -> None:
        """Download the containers by container type for each task in the specified job"""

        self.onefuzz.containers.download_job(job_id, output=output)

    def rerun(
        self,
        job_id: UUID_EXPANSION,
        *,
        duration: Optional[int] = None,
        pool_name: Optional[PoolName] = None,
    ) -> Job:
        """rerun a given job"""

        existing_job = self.onefuzz.jobs.get(job_id)
        job_config = existing_job.config
        if duration is not None:
            job_config.duration = duration
        existing_tasks = self.onefuzz.jobs.tasks.list(existing_job.job_id)

        configs: Dict[UUID, TaskConfig] = {}
        todo: Set[UUID] = set()
        new_task_ids: Dict[UUID, UUID] = {}

        for task in existing_tasks:
            config = TaskConfig.parse_obj(json.loads(task.config.json()))
            if pool_name is not None and config.pool is not None:
                config.pool.pool_name = pool_name
            configs[task.task_id] = config
            todo.add(task.task_id)

        job = self.onefuzz.jobs.create_with_config(existing_job.config)

        while todo:
            added: Set[UUID] = set()

            for task_id in todo:
                config = configs[task_id]
                config.job_id = job.job_id
                if config.prereq_tasks:
                    if set(config.prereq_tasks).issubset(new_task_ids.keys()):
                        config.prereq_tasks = [
                            new_task_ids[x] for x in config.prereq_tasks
                        ]
                        task = self.onefuzz.tasks.create_with_config(config)
                        self.logger.info(
                            "created task: %s - %s",
                            task.task_id,
                            task.config.task.type.name,
                        )
                        new_task_ids[task_id] = task.task_id
                        added.add(task_id)
                else:
                    task = self.onefuzz.tasks.create_with_config(config)
                    self.logger.info(
                        "created task: %s - %s",
                        task.task_id,
                        task.config.task.type.name,
                    )
                    new_task_ids[task_id] = task.task_id
                    added.add(task_id)

            if added:
                todo -= added
            else:
                raise Exception(f"unable to resolve task prereqs for: {todo}")

        return job


class DebugLog(Command):
    def __init__(self, onefuzz: "Onefuzz", logger: logging.Logger):
        self.onefuzz = onefuzz
        self.logger = logger
        self._client: Optional[ApplicationInsightsDataClient] = None
        self._app_id: Optional[str] = None

    def _convert(self, raw_data: Any) -> Union[Dict[str, Any], List[Dict[str, Any]]]:
        results = {}
        for table in raw_data.tables:
            result = []
            for row in table.rows:
                converted = {
                    table.columns[x].name: y
                    for (x, y) in enumerate(row)
                    if y not in [None, ""]
                }
                if "customDimensions" in converted:
                    converted["customDimensions"] = json.loads(
                        converted["customDimensions"]
                    )
                result.append(converted)
            results[table.name] = result

        if list(results.keys()) == ["PrimaryResult"]:
            return results["PrimaryResult"]

        return results

    def query(
        self,
        log_query: str,
        *,
        timespan: Optional[str] = DAY_TIMESPAN,
        raw: bool = False,
    ) -> Any:
        """
        Perform an Application Insights query

        Queries should be well formed Kusto Queries.
        Ref https://docs.microsoft.com/en-us/azure/data-explorer/kql-quick-reference

        :param str log_query: Query to send to Application Insights
        :param str timespan: ISO 8601 duration format
        :param bool raw: Do not simplify the data result
        """
        if self._app_id is None:
            self._app_id = self.onefuzz.info.get().insights_appid
        if self._app_id is None:
            raise Exception("instance does not have an insights_appid")
        if self._client is None:
            creds = AzureIdentityCredentialAdapter(
                AzureCliCredential(), resource_id="https://api.applicationinsights.io"
            )
            self._client = ApplicationInsightsDataClient(creds)

        self.logger.debug("query: %s", log_query)
        raw_data = self._client.query.execute(
            self._app_id, body=QueryBody(query=log_query, timespan=timespan)
        )
        if "error" in raw_data.additional_properties:
            raise Exception(
                "Error performing query: %s" % raw_data.additional_properties["error"]
            )
        if raw:
            return raw_data
        return self._convert(raw_data)

    def _query_parts(
        self, parts: List[str], *, timespan: Optional[str] = None, raw: bool = False
    ) -> Any:
        log_query = " | ".join(parts)
        return self.query(log_query, timespan=timespan, raw=raw)

    def _build_keyword_query(
        self, value: str, limit: Optional[int] = None, desc: bool = True
    ) -> List[str]:
        # See https://docs.microsoft.com/en-us/azure/data-explorer/kql-quick-reference

        components = ["union isfuzzy=true exceptions, traces, customEvents"]
        value = value.strip()
        keywords = ['* has "%s"' % (x.replace('"', '\\"')) for x in value.split(" ")]
        if keywords:
            components.append("where " + " and ".join(keywords))
        order = "desc" if desc else "asc"
        if limit:
            components.append(f"take {limit}")
        components.append(f"order by timestamp {order}")
        return components

    def keyword(
        self,
        value: str,
        *,
        timespan: Optional[str] = DAY_TIMESPAN,
        limit: Optional[int] = None,
        raw: bool = False,
    ) -> Any:
        """
        Perform an Application Insights keyword query akin to "Transaction Search"

        :param str value: Keyword to query Application Insights
        :param str timespan: ISO 8601 duration format
        :param int limit: Limit the number of records returned
        :param bool raw: Do not simplify the data result
        """

        components = self._build_keyword_query(value, limit=limit)

        return self._query_parts(components, timespan=timespan, raw=raw)

    def tail(
        self,
        value: str,
        *,
        limit: int = 1000,
        indent: Optional[int] = None,
        filter: Optional[str] = "[message, name, customDimensions]",
        timespan: Optional[str] = HOUR_TIMESPAN,
    ) -> None:
        """
        Perform an Application Insights keyword query akin to "Transaction Search"

        :param str value: Keyword to query Application Insights
        :param str indent: Specify indent for JSON printing
        :param str limit: Limit the number of records to return in each query
        :param str filter: JMESPath filter for streaming results
        """

        expression = None
        if filter:
            expression = jmespath.compile(filter)

        base_query = self._build_keyword_query(value, limit=limit, desc=False)

        last_seen: Optional[str] = None
        wait = DEFAULT_TAIL_DELAY

        while True:
            query = base_query.copy()
            if last_seen is not None:
                query.append(f'where timestamp > datetime("{last_seen}")')
            results = self._query_parts(query, timespan=timespan)

            if results:
                last_seen = results[-1]["timestamp"]
                for entry in results:
                    if expression is not None:
                        entry = expression.search(entry)
                    if entry:
                        print(json.dumps(entry, indent=indent, sort_keys=True))
                wait = DEFAULT_TAIL_DELAY
            else:
                self.onefuzz.logger.debug("waiting %f seconds", wait)

                time.sleep(wait)
                if wait < 60:
                    wait *= 1.5

    def _query_libfuzzer_coverage(
        self, query: str, timespan: str, limit: Optional[int] = None
    ) -> Any:
        project_fields = [
            "rate=customDimensions.rate",
            "covered=customDimensions.covered",
            "features=customDimensions.features",
            "timestamp",
        ]

        query_parts = [
            "customEvents",
            "where name == 'coverage_data'",
            query,
            "order by timestamp desc",
            f"project {','.join(project_fields)}",
        ]

        if limit:
            query_parts.append(f"take {limit}")

        return self.onefuzz.debug.logs._query_parts(query_parts, timespan=timespan)

    def _query_libfuzzer_execs_sec(
        self,
        query: str,
        timespan: str,
        limit: Optional[int] = None,
    ) -> Any:
        project_fields = [
            "machine_id=customDimensions.machine_id",
            "worker_id=customDimensions.worker_id",
            "execs_sec=customDimensions.execs_sec",
            "timestamp",
        ]

        query_parts = [
            "customEvents",
            "where name == 'runtime_stats'",
            query,
            "where customDimensions.execs_sec > 0",
            "order by timestamp desc",
            f"project {','.join(project_fields)}",
        ]
        if limit:
            query_parts.append(f"take {limit}")

        return self.onefuzz.debug.logs._query_parts(query_parts, timespan=timespan)

    def get(
        self,
        job_id: Optional[str],
        task_id: Optional[str],
        machine_id: Optional[str],
        last: Optional[int] = 1,
        all: bool = False,
    ) -> None:
        """
        Download the latest agent logs.
        Make sure you have Storage Blob Data Reader permission.

        :param str job_id: Which job you would like the logs for.
        :param str task_id: Which task you would like the logs for.
        :param str machine_id: Which machine you would like the logs for.
        :param int last: The logs are split in files. Starting with the newest files, how many files you would you like to download.
        :param bool all: Download all log files.
        """

        from typing import cast
        from urllib import parse

        if job_id is None:
            if task_id is None:
                raise Exception("You need to specify at least one of job_id or task_id")

            task = self.onefuzz.tasks.get(task_id)
            job_id = str(task.job_id)

        job = self.onefuzz.jobs.get(job_id)
        container_url = job.config.logs

        if container_url is None:
            raise Exception(
                f"Job with id {job_id} does not have a logging location configured"
            )

        file_path = None
        if task_id is not None:
            file_path = f"{task_id}/"

            if machine_id is not None:
                file_path += f"{machine_id}/"

        container_name = parse.urlsplit(container_url).path[1:]
        container = self.onefuzz.containers.get(container_name)

        container_client: ContainerClient = ContainerClient.from_container_url(
            container.sas_url
        )

        blobs = container_client.list_blobs(name_starts_with=file_path)

        class CustomFile:
            def __init__(self, name: str, creation_time: datetime):
                self.name = name
                self.creation_time = creation_time

        files: List[CustomFile] = []

        for f in blobs:
            if f.creation_time is not None:
                creation_time = cast(datetime, f.creation_time)
            else:
                creation_time = datetime.min

            files.append(CustomFile(f.name, creation_time))

        files.sort(key=lambda x: x.creation_time, reverse=True)

        num_files = len(files)
        if num_files > 0:
            self.logger.info(f"Found {len(files)} matching files to download")
        else:
            self.logger.info("Did not find any matching files to download")
            return None

        if not all:
            self.logger.info(f"Downloading only the {last} most recent files")
            files = files[:last]

        for f in files:
            self.logger.info(f"Downloading {f.name}")

            local_path = os.path.join(os.getcwd(), f.name)
            local_directory = os.path.dirname(local_path)
            if not os.path.exists(local_directory):
                os.makedirs(local_directory)

            with open(local_path, "wb") as download_file:
                data = container_client.download_blob(f.name)
                data.readinto(download_file)

        return None


class DebugNotification(Command):
    """Debug notification integrations"""

    def _get_container(
        self, task: Task, container_type: ContainerType
    ) -> Optional[Container]:
        if task.config.containers is not None:
            for container in task.config.containers:
                if container.type == container_type:
                    return container.name
        return None

    def _get_storage_account(self, container_name: Container) -> str:
        sas_url = self.onefuzz.containers.get(container_name).sas_url
        _, netloc, _, _, _, _ = urlparse(sas_url)
        return netloc.split(".")[0]

    def template(
        self, template: str, context: Optional[models.TemplateRenderContext]
    ) -> TemplateValidationResponse:
        """Validate scriban rendering of notification config"""
        req = requests.TemplateValidationPost(template=template, context=context)
        return self.onefuzz.validate_scriban.post(req)

    def job(
        self,
        job_id: UUID_EXPANSION,
        *,
        report_container_type: ContainerType = ContainerType.unique_reports,
        crash_name: str = "fake-crash-sample",
    ) -> None:
        """Inject a report into the first crash reporting task in the specified job"""

        tasks = self.onefuzz.tasks.list(job_id=job_id, state=[])
        for task in tasks:
            if task.config.task.type in [
                TaskType.libfuzzer_crash_report,
                TaskType.generic_crash_report,
            ]:
                self.task(
                    str(task.task_id),
                    report_container_type=report_container_type,
                    crash_name=crash_name,
                )
                return

        raise Exception("no crash reporting tasks configured")

    def task(
        self,
        task_id: UUID_EXPANSION,
        *,
        report_container_type: ContainerType = ContainerType.unique_reports,
        crash_name: str = "fake-crash-sample",
    ) -> None:
        """Inject a report into the specified crash reporting task"""

        task = self.onefuzz.tasks.get(task_id)

        crashes = self._get_container(task, ContainerType.crashes)
        reports = self._get_container(task, report_container_type)

        if crashes is None:
            raise Exception("task does not have a crashes container")

        if reports is None:
            raise Exception(
                "task does not have a %s container" % report_container_type.name
            )

        with tempfile.TemporaryDirectory() as tempdir:
            file_path = os.path.join(tempdir, crash_name)
            with open(file_path, "w") as handle:
                handle.write("")
            self.onefuzz.containers.files.upload_file(crashes, file_path, crash_name)

        input_blob_ref = BlobRef(
            account=self._get_storage_account(crashes),
            container=crashes,
            name=crash_name,
        )

        assert task.config.task.target_exe is not None
        report = self._create_report(
            task.job_id, task.task_id, task.config.task.target_exe, input_blob_ref
        )

        with tempfile.TemporaryDirectory() as tempdir:
            file_path = os.path.join(tempdir, "report.json")
            with open(file_path, "w") as handle:
                handle.write(report.json())

            self.onefuzz.containers.files.upload_file(
                reports, file_path, crash_name + ".json"
            )

    def test_template(
        self, task_id: UUID_EXPANSION, template: models.NotificationConfig
    ) -> responses.NotificationTestResponse:
        """Test a notification template"""
        endpoint = Endpoint(self.onefuzz)
        task = self.onefuzz.tasks.get(task_id)
        input_blob_ref = BlobRef(
            account="dummy-storage-account",
            container="test-notification-crashes",
            name="fake-crash-sample",
        )

        report = self._create_report(
            task.job_id, task.task_id, "fake_target.exe", input_blob_ref
        )
        report.report_url = "https://dummy-container.blob.core.windows.net/dummy-reports/dummy-report.json"

        return endpoint._req_model(
            "POST",
            responses.NotificationTestResponse,
            data=requests.NotificationTest(
                report=report,
                notification=models.Notification(
                    container=Container("test-notification-reports"),
                    notification_id=uuid.uuid4(),
                    config=template.config,
                ),
            ),
            alternate_endpoint="notifications/test",
        )

    def _create_report(
        self, job_id: UUID, task_id: UUID, target_exe: str, input_blob_ref: BlobRef
    ) -> Report:
        return Report(
            input_blob=input_blob_ref,
            executable=target_exe,
            crash_type="fake crash report",
            crash_site="fake crash site",
            call_stack=["#0 fake", "#1 call", "#2 stack"],
            call_stack_sha256=ZERO_SHA256,
            input_sha256=EMPTY_SHA256,
            asan_log="fake asan log",
            task_id=task_id,
            job_id=job_id,
            minimized_stack=[],
            minimized_stack_function_names=[],
            tool_name="libfuzzer",
            tool_version="1.2.3",
            onefuzz_version="1.2.3",
        )


class Debug(Command):
    """Debug running jobs"""

    def __init__(self, onefuzz: Any, logger: logging.Logger):
        super().__init__(onefuzz, logger)
        self.scalesets = DebugScaleset(onefuzz, logger)
        self.repro = DebugRepro(onefuzz, logger)
        self.job = DebugJob(onefuzz, logger)
        self.notification = DebugNotification(onefuzz, logger)
        self.task = DebugTask(onefuzz, logger)
        self.logs = DebugLog(onefuzz, logger)
        self.node = DebugNode(onefuzz, logger)
