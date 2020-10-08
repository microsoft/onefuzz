#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import os
from typing import List, Optional
from uuid import UUID

from onefuzztypes.enums import OS, AgentMode
from onefuzztypes.models import AgentConfig, ReproConfig
from onefuzztypes.primitives import Extension, Region

from .azure.containers import get_container_sas_url, get_file_sas_url, save_blob
from .azure.creds import get_func_storage, get_instance_url
from .azure.monitor import get_monitor_settings
from .reports import get_report

# TODO: figure out how to create VM specific SSH keys for Windows.
#
# Previously done via task specific scripts:

# if is_windows and auth is not None:
#     ssh_key = auth.public_key.strip()
#     ssh_path = "$env:ProgramData/ssh/administrators_authorized_keys"
#     commands += ['Set-Content -Path %s -Value "%s"' % (ssh_path, ssh_key)]
#     return commands


def generic_extensions(region: Region, os: OS) -> List[Extension]:
    extensions = [monitor_extension(region, os)]
    depedency = dependency_extension(region, os)
    if depedency:
        extensions.append(depedency)

    return extensions


def monitor_extension(region: Region, os: OS) -> Extension:
    settings = get_monitor_settings()

    if os == OS.windows:
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
    elif os == OS.linux:
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
    raise NotImplementedError("unsupported os: %s" % os)


def dependency_extension(region: Region, os: OS) -> Optional[Extension]:
    if os == OS.windows:
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


def build_pool_config(pool_name: str) -> str:
    agent_config = AgentConfig(
        pool_name=pool_name,
        onefuzz_url=get_instance_url(),
        instrumentation_key=os.environ.get("APPINSIGHTS_INSTRUMENTATIONKEY"),
        telemetry_key=os.environ.get("ONEFUZZ_TELEMETRY"),
    )

    save_blob(
        "vm-scripts",
        "%s/config.json" % pool_name,
        agent_config.json(),
        account_id=get_func_storage(),
    )

    return get_file_sas_url(
        "vm-scripts",
        "%s/config.json" % pool_name,
        account_id=get_func_storage(),
        read=True,
    )


def update_managed_scripts(mode: AgentMode) -> None:
    commands = [
        "azcopy sync '%s' instance-specific-setup"
        % (
            get_container_sas_url(
                "instance-specific-setup",
                read=True,
                list=True,
                account_id=get_func_storage(),
            )
        ),
        "azcopy sync '%s' tools"
        % (
            get_container_sas_url(
                "tools", read=True, list=True, account_id=get_func_storage()
            )
        ),
    ]

    save_blob(
        "vm-scripts",
        "managed.ps1",
        "\r\n".join(commands) + "\r\n",
        account_id=get_func_storage(),
    )
    save_blob(
        "vm-scripts",
        "managed.sh",
        "\n".join(commands) + "\n",
        account_id=get_func_storage(),
    )


def agent_config(
    region: Region, os: OS, mode: AgentMode, *, urls: Optional[List[str]] = None
) -> Extension:
    update_managed_scripts(mode)

    if urls is None:
        urls = []

    if os == OS.windows:
        urls += [
            get_file_sas_url(
                "vm-scripts",
                "managed.ps1",
                account_id=get_func_storage(),
                read=True,
            ),
            get_file_sas_url(
                "tools",
                "win64/azcopy.exe",
                account_id=get_func_storage(),
                read=True,
            ),
            get_file_sas_url(
                "tools",
                "win64/setup.ps1",
                account_id=get_func_storage(),
                read=True,
            ),
            get_file_sas_url(
                "tools",
                "win64/onefuzz.ps1",
                account_id=get_func_storage(),
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
    elif os == OS.linux:
        urls += [
            get_file_sas_url(
                "vm-scripts",
                "managed.sh",
                account_id=get_func_storage(),
                read=True,
            ),
            get_file_sas_url(
                "tools",
                "linux/azcopy",
                account_id=get_func_storage(),
                read=True,
            ),
            get_file_sas_url(
                "tools",
                "linux/setup.sh",
                account_id=get_func_storage(),
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

    raise NotImplementedError("unsupported OS: %s" % os)


def fuzz_extensions(region: Region, os: OS, pool_name: str) -> List[Extension]:
    urls = [build_pool_config(pool_name)]
    fuzz_extension = agent_config(region, os, AgentMode.fuzz, urls=urls)
    extensions = generic_extensions(region, os)
    extensions += [fuzz_extension]
    return extensions


def repro_extensions(
    region: Region,
    repro_os: OS,
    repro_id: UUID,
    repro_config: ReproConfig,
    setup_container: Optional[str],
) -> List[Extension]:
    # TODO - what about contents of repro.ps1 / repro.sh?
    report = get_report(repro_config.container, repro_config.path)
    if report is None:
        raise Exception("invalid report: %s" % repro_config)

    commands = []
    if setup_container:
        commands += [
            "azcopy sync '%s' ./setup"
            % (get_container_sas_url(setup_container, read=True, list=True)),
        ]

    urls = [
        get_file_sas_url(repro_config.container, repro_config.path, read=True),
        get_file_sas_url(
            report.input_blob.container, report.input_blob.name, read=True
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
        "task-configs",
        "%s/%s" % (repro_id, script_name),
        task_script,
        account_id=get_func_storage(),
    )

    for repro_file in repro_files:
        urls += [
            get_file_sas_url(
                "repro-scripts",
                repro_file,
                account_id=get_func_storage(),
                read=True,
            ),
            get_file_sas_url(
                "task-configs",
                "%s/%s" % (repro_id, script_name),
                account_id=get_func_storage(),
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
            "proxy-configs",
            "%s/config.json" % region,
            account_id=get_func_storage(),
            read=True,
        ),
        get_file_sas_url(
            "tools",
            "linux/onefuzz-proxy-manager",
            account_id=get_func_storage(),
            read=True,
        ),
    ]

    base_extension = agent_config(region, OS.linux, AgentMode.proxy, urls=urls)
    extensions = generic_extensions(region, OS.linux)
    extensions += [base_extension]
    return extensions
