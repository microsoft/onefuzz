#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import os
from typing import List, Optional
from uuid import UUID

from onefuzztypes.enums import OS, AgentMode
from onefuzztypes.models import AgentConfig, Pool, ReproConfig, Scaleset
from onefuzztypes.primitives import Container, Extension, Region

from .azure.containers import get_container_sas_url, get_file_sas_url, save_blob
from .azure.creds import get_instance_id, get_instance_url
from .azure.monitor import get_monitor_settings
from .azure.queue import get_queue_sas
from .azure.storage import StorageType
from .reports import get_report


def generic_extensions(region: Region, vm_os: OS) -> List[Extension]:
    extensions = [monitor_extension(region, vm_os)]
    depedency = dependency_extension(region, vm_os)
    if depedency:
        extensions.append(depedency)

    return extensions


def monitor_extension(region: Region, vm_os: OS) -> Extension:
    settings = get_monitor_settings()

    if vm_os == OS.windows:
        return {
            "name": "OMSExtension",
            "publisher": "Microsoft.EnterpriseCloud.Monitoring",
            "type": "MicrosoftMonitoringAgent",
            "typeHandlerVersion": "1.0",
            "location": region,
            "autoUpgradeMinorVersion": True,
            "settings": {"workspaceId": settings["id"]},
            "protectedSettings": {"workspaceKey": settings["key"]},
        }
    elif vm_os == OS.linux:
        return {
            "name": "OMSExtension",
            "publisher": "Microsoft.EnterpriseCloud.Monitoring",
            "type": "OmsAgentForLinux",
            "typeHandlerVersion": "1.12",
            "location": region,
            "autoUpgradeMinorVersion": True,
            "settings": {"workspaceId": settings["id"]},
            "protectedSettings": {"workspaceKey": settings["key"]},
        }
    raise NotImplementedError("unsupported os: %s" % vm_os)


def dependency_extension(region: Region, vm_os: OS) -> Optional[Extension]:
    if vm_os == OS.windows:
        extension = {
            "name": "DependencyAgentWindows",
            "publisher": "Microsoft.Azure.Monitoring.DependencyAgent",
            "type": "DependencyAgentWindows",
            "typeHandlerVersion": "9.5",
            "location": region,
            "autoUpgradeMinorVersion": True,
        }
        return extension
    else:
        # TODO: dependency agent for linux is not reliable
        # extension = {
        #     "name": "DependencyAgentLinux",
        #     "publisher": "Microsoft.Azure.Monitoring.DependencyAgent",
        #     "type": "DependencyAgentLinux",
        #     "typeHandlerVersion": "9.5",
        #     "location": vm.region,
        #     "autoUpgradeMinorVersion": True,
        # }
        return None


def build_scaleset_script(pool: Pool, scaleset: Scaleset) -> str:
    commands = []
    extension = "ps1" if pool.os == OS.windows else "sh"
    filename = f"{scaleset.scaleset_id}/scaleset-setup.{extension}"
    sep = "\r\n" if pool.os == OS.windows else "\n"

    if pool.os == OS.windows and scaleset.auth is not None:
        ssh_key = scaleset.auth.public_key.strip()
        ssh_path = "$env:ProgramData/ssh/administrators_authorized_keys"
        commands += [f'Set-Content -Path {ssh_path} -Value "{ssh_key}"']

    save_blob(
        Container("vm-scripts"), filename, sep.join(commands) + sep, StorageType.config
    )
    return get_file_sas_url(
        Container("vm-scripts"), filename, StorageType.config, read=True
    )


def build_pool_config(pool: Pool) -> str:
    config = AgentConfig(
        pool_name=pool.name,
        onefuzz_url=get_instance_url(),
        instrumentation_key=os.environ.get("APPINSIGHTS_INSTRUMENTATIONKEY"),
        heartbeat_queue=get_queue_sas(
            "node-heartbeat",
            StorageType.config,
            add=True,
        ),
        telemetry_key=os.environ.get("ONEFUZZ_TELEMETRY"),
        instance_id=get_instance_id(),
    )

    filename = f"{pool.name}/config.json"

    save_blob(
        Container("vm-scripts"),
        filename,
        config.json(),
        StorageType.config,
    )

    return get_file_sas_url(
        Container("vm-scripts"),
        filename,
        StorageType.config,
        read=True,
    )


def update_managed_scripts() -> None:
    commands = [
        "azcopy sync '%s' instance-specific-setup"
        % (
            get_container_sas_url(
                Container("instance-specific-setup"),
                StorageType.config,
                read=True,
                list=True,
            )
        ),
        "azcopy sync '%s' tools"
        % (
            get_container_sas_url(
                Container("tools"), StorageType.config, read=True, list=True
            )
        ),
    ]

    save_blob(
        Container("vm-scripts"),
        "managed.ps1",
        "\r\n".join(commands) + "\r\n",
        StorageType.config,
    )
    save_blob(
        Container("vm-scripts"),
        "managed.sh",
        "\n".join(commands) + "\n",
        StorageType.config,
    )


def agent_config(
    region: Region, vm_os: OS, mode: AgentMode, *, urls: Optional[List[str]] = None
) -> Extension:
    update_managed_scripts()

    if urls is None:
        urls = []

    if vm_os == OS.windows:
        urls += [
            get_file_sas_url(
                Container("vm-scripts"),
                "managed.ps1",
                StorageType.config,
                read=True,
            ),
            get_file_sas_url(
                Container("tools"),
                "win64/azcopy.exe",
                StorageType.config,
                read=True,
            ),
            get_file_sas_url(
                Container("tools"),
                "win64/setup.ps1",
                StorageType.config,
                read=True,
            ),
            get_file_sas_url(
                Container("tools"),
                "win64/onefuzz.ps1",
                StorageType.config,
                read=True,
            ),
        ]
        to_execute_cmd = (
            "powershell -ExecutionPolicy Unrestricted -File win64/setup.ps1 "
            "-mode %s" % (mode.name)
        )
        extension = {
            "name": "CustomScriptExtension",
            "type": "CustomScriptExtension",
            "publisher": "Microsoft.Compute",
            "location": region,
            "type_handler_version": "1.9",
            "auto_upgrade_minor_version": True,
            "settings": {"commandToExecute": to_execute_cmd, "fileUris": urls},
            "protectedSettings": {},
        }
        return extension
    elif vm_os == OS.linux:
        urls += [
            get_file_sas_url(
                Container("vm-scripts"),
                "managed.sh",
                StorageType.config,
                read=True,
            ),
            get_file_sas_url(
                Container("tools"),
                "linux/azcopy",
                StorageType.config,
                read=True,
            ),
            get_file_sas_url(
                Container("tools"),
                "linux/setup.sh",
                StorageType.config,
                read=True,
            ),
        ]
        to_execute_cmd = "sh setup.sh %s" % (mode.name)

        extension = {
            "name": "CustomScript",
            "publisher": "Microsoft.Azure.Extensions",
            "type": "CustomScript",
            "typeHandlerVersion": "2.1",
            "location": region,
            "autoUpgradeMinorVersion": True,
            "settings": {"commandToExecute": to_execute_cmd, "fileUris": urls},
            "protectedSettings": {},
        }
        return extension

    raise NotImplementedError("unsupported OS: %s" % vm_os)


def fuzz_extensions(pool: Pool, scaleset: Scaleset) -> List[Extension]:
    urls = [build_pool_config(pool), build_scaleset_script(pool, scaleset)]
    fuzz_extension = agent_config(scaleset.region, pool.os, AgentMode.fuzz, urls=urls)
    extensions = generic_extensions(scaleset.region, pool.os)
    extensions += [fuzz_extension]
    return extensions


def repro_extensions(
    region: Region,
    repro_os: OS,
    repro_id: UUID,
    repro_config: ReproConfig,
    setup_container: Optional[Container],
) -> List[Extension]:
    # TODO - what about contents of repro.ps1 / repro.sh?
    report = get_report(repro_config.container, repro_config.path)
    if report is None:
        raise Exception("invalid report: %s" % repro_config)

    if report.input_blob is None:
        raise Exception("unable to perform reproduction without an input blob")

    commands = []
    if setup_container:
        commands += [
            "azcopy sync '%s' ./setup"
            % (
                get_container_sas_url(
                    setup_container, StorageType.corpus, read=True, list=True
                )
            ),
        ]

    urls = [
        get_file_sas_url(
            repro_config.container, repro_config.path, StorageType.corpus, read=True
        ),
        get_file_sas_url(
            report.input_blob.container,
            report.input_blob.name,
            StorageType.corpus,
            read=True,
        ),
    ]

    repro_files = []
    if repro_os == OS.windows:
        repro_files = ["%s/repro.ps1" % repro_id]
        task_script = "\r\n".join(commands)
        script_name = "task-setup.ps1"
    else:
        repro_files = ["%s/repro.sh" % repro_id, "%s/repro-stdout.sh" % repro_id]
        commands += ["chmod -R +x setup"]
        task_script = "\n".join(commands)
        script_name = "task-setup.sh"

    save_blob(
        Container("task-configs"),
        "%s/%s" % (repro_id, script_name),
        task_script,
        StorageType.config,
    )

    for repro_file in repro_files:
        urls += [
            get_file_sas_url(
                Container("repro-scripts"),
                repro_file,
                StorageType.config,
                read=True,
            ),
            get_file_sas_url(
                Container("task-configs"),
                "%s/%s" % (repro_id, script_name),
                StorageType.config,
                read=True,
            ),
        ]

    base_extension = agent_config(region, repro_os, AgentMode.repro, urls=urls)
    extensions = generic_extensions(region, repro_os)
    extensions += [base_extension]
    return extensions


def proxy_manager_extensions(region: Region) -> List[Extension]:
    urls = [
        get_file_sas_url(
            Container("proxy-configs"),
            "%s/config.json" % region,
            StorageType.config,
            read=True,
        ),
        get_file_sas_url(
            Container("tools"),
            "linux/onefuzz-proxy-manager",
            StorageType.config,
            read=True,
        ),
    ]

    base_extension = agent_config(region, OS.linux, AgentMode.proxy, urls=urls)
    extensions = generic_extensions(region, OS.linux)
    extensions += [base_extension]
    return extensions
