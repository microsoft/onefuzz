#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import os
from typing import List, Optional
from uuid import UUID, uuid4

from onefuzztypes.enums import OS, AgentMode, ScalesetExtension
from onefuzztypes.models import AgentConfig, Pool, ReproConfig, Scaleset
from onefuzztypes.primitives import Container, Extension, Region

from .azure.containers import (
    get_container_sas_url,
    get_file_sas_url,
    get_file_url,
    save_blob,
)
from .azure.creds import get_instance_id, get_instance_url
from .azure.monitor import get_monitor_settings
from .azure.queue import get_queue_sas
from .azure.storage import StorageType
from .reports import get_report

DEFAULT_EXTENSIONS = [
    ScalesetExtension.OMSExtension,
    ScalesetExtension.DependencyAgent,
    ScalesetExtension.CustomScriptExtension,
]


def generic_extensions(
    region: Region,
    extension_list: List[ScalesetExtension],
    vm_os: OS,
    cert_key: Optional[str],
    cert: Optional[str],
) -> List[Extension]:
    extensions = []
    depedency = dependency_extension(region, vm_os)
    if depedency and ScalesetExtension.OMSExtension in extension_list:
        extensions.append(depedency)
    monitor = monitor_extension(region, vm_os)
    if monitor and ScalesetExtension.DependencyAgent in extension_list:
        extensions.append(monitor)
    geneva = geneva_extension(region, vm_os)
    if (
        geneva
        and ScalesetExtension.GenevaMonitoring in extension_list
        and vm_os == OS.windows
    ):
        extensions.append(geneva)
    azmon = azmon_extension(region, vm_os, cert_key, cert)
    azsec = azsec_extension(region, vm_os)
    if (
        azmon
        and azsec
        and ScalesetExtension.GenevaMonitoring in extension_list
        and vm_os == OS.linux
    ):
        extensions.append(azmon)
        extensions.append(azsec)
    keyvault = keyvault_extension(region, vm_os)
    if keyvault and ScalesetExtension.KeyvaultExtension in extension_list:
        extensions.append(keyvault)

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
    raise NotImplementedError("unsupported os : %s" % vm_os)


def geneva_extension(region: Region, vm_os: OS) -> Extension:
    return {
        "name": "Microsoft.Azure.Geneva.GenevaMonitoring",
        "publisher": "Microsoft.Azure.Geneva",
        "type": "GenevaMonitoring",
        "typeHandlerVersion": "2.0",
        "location": region,
        "autoUpgradeMinorVersion": True,
        "enableAutomaticUpgrade": True,
        "settings": {},
        "protectedSettings": {},
    }


def azmon_extension(
    region: Region, vm_os: OS, cert_key: Optional[str], cert: Optional[str]
) -> Extension:
    auth_id = "airseccosinetest.geneva.keyvault.airaspcerts.cloudapp.net"
    return {
        "name": "AzureMonitorLinuxAgent",
        "publisher": "Microsoft.Azure.Monitor",
        "location": region,
        "type": "AzureMonitorLinuxAgent",
        "typeHandlerVersion": "1.0",
        "autoUpgradeMinorVersion": True,
        "settings": {},
        "protectedsettings": {
            "configVersion": "1.0",
            "moniker": "edgsecfuzzingpmeprod",
            "namespace": "linuxpmeprod",
            "monitoringGCSEnvironment": "DiagnosticsProd",
            "monitoringGCSAccount": "edgsecfuzzingpmeprod",
            "monitoringGCSRegion": "westus2",
            "monitoringGCSAuthId": auth_id,
            "monitoringGCSAuthIdType": "AuthKeyVault",
        },
    }


def azsec_extension(region: Region, vm_os: OS) -> Extension:
    return {
        "name": "AzureSecurityLinuxAgent",
        "publisher": "Microsoft.Azure.Security.Monitoring",
        "location": region,
        "type": "AzureSecurityLinuxAgent",
        "typeHandlerVersion": "2.0",
        "autoUpgradeMinorVersion": True,
        "settings": {"enableGenevaUpload": True},
    }


def keyvault_extension(region: Region, vm_os: OS) -> Extension:
    keyvault = "https://azure-policy-test-kv.vault.azure.net/secrets/"
    cert = "Geneva-Test-Cert"
    uri = keyvault + cert
    if vm_os == OS.windows:
        return {
            "name": "KVVMExtensionForWindows",
            "location": region,
            "publisher": "Microsoft.Azure.KeyVault",
            "type": "KeyVaultForWindows",
            "typeHandlerVersion": "1.0",
            "autoUpgradeMinorVersion": True,
            "settings": {
                "secretsManagementSettings": {
                    "pollingIntervalInS": "3600",
                    "certificateStoreName": "MY",
                    "linkOnRenewal": False,
                    "certificateStoreLocation": "LocalMachine",
                    "requireInitialSync": True,
                    "observedCertificates": [uri],
                }
            },
        }
    elif vm_os == OS.linux:
        cert_path = "/var/lib/waagent/"
        extension = "Microsoft.Azure.KeyVault.Store"
        location = cert_path + extension
        return {
            "name": "KVVMExtensionForLinux",
            "location": region,
            "publisher": "Microsoft.Azure.KeyVault",
            "type": "KeyVaultForLinux",
            "typeHandlerVersion": "2.0",
            "autoUpgradeMinorVersion": True,
            "settings": {
                "secretsManagementSettings": {
                    "pollingIntervalInS": "3600",
                    "certificateStoreLocation": location,
                    "observedCertificates": [uri],
                },
            },
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
    return get_file_url(Container("vm-scripts"), filename, StorageType.config)


def build_pool_config(pool: Pool) -> str:
    config = AgentConfig(
        pool_name=pool.name,
        onefuzz_url=get_instance_url(),
        heartbeat_queue=get_queue_sas(
            "node-heartbeat",
            StorageType.config,
            add=True,
        ),
        instance_telemetry_key=os.environ.get("APPINSIGHTS_INSTRUMENTATIONKEY"),
        microsoft_telemetry_key=os.environ.get("ONEFUZZ_TELEMETRY"),
        instance_id=get_instance_id(),
    )

    multi_tenant_domain = os.environ.get("MULTI_TENANT_DOMAIN")
    if multi_tenant_domain:
        config.multi_tenant_domain = multi_tenant_domain

    filename = f"{pool.name}/config.json"

    save_blob(
        Container("vm-scripts"),
        filename,
        config.json(),
        StorageType.config,
    )

    return config_url(Container("vm-scripts"), filename, False)


def update_managed_scripts() -> None:
    commands = [
        "azcopy sync '%s' instance-specific-setup"
        % (
            get_container_sas_url(
                Container("instance-specific-setup"),
                StorageType.config,
                read=True,
                list_=True,
            )
        ),
        "azcopy sync '%s' tools"
        % (
            get_container_sas_url(
                Container("tools"), StorageType.config, read=True, list_=True
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


def config_url(container: Container, filename: str, with_sas: bool) -> str:
    if with_sas:
        return get_file_sas_url(container, filename, StorageType.config, read=True)
    else:
        return get_file_url(container, filename, StorageType.config)


def agent_config(
    region: Region,
    vm_os: OS,
    mode: AgentMode,
    *,
    urls: Optional[List[str]] = None,
    with_sas: bool = False,
) -> Extension:
    update_managed_scripts()

    if urls is None:
        urls = []

    if vm_os == OS.windows:
        urls += [
            config_url(Container("vm-scripts"), "managed.ps1", with_sas),
            config_url(Container("tools"), "win64/azcopy.exe", with_sas),
            config_url(
                Container("tools"),
                "win64/setup.ps1",
                with_sas,
            ),
            config_url(
                Container("tools"),
                "win64/onefuzz.ps1",
                with_sas,
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
            "force_update_tag": uuid4(),
            "type_handler_version": "1.9",
            "auto_upgrade_minor_version": True,
            "settings": {
                "commandToExecute": to_execute_cmd,
                "fileUris": urls,
            },
            "protectedSettings": {
                "managedIdentity": {},
            },
        }
        return extension
    elif vm_os == OS.linux:
        urls += [
            config_url(
                Container("vm-scripts"),
                "managed.sh",
                with_sas,
            ),
            config_url(
                Container("tools"),
                "linux/azcopy",
                with_sas,
            ),
            config_url(
                Container("tools"),
                "linux/setup.sh",
                with_sas,
            ),
        ]
        to_execute_cmd = "sh setup.sh %s" % (mode.name)

        extension = {
            "name": "CustomScript",
            "publisher": "Microsoft.Azure.Extensions",
            "type": "CustomScript",
            "typeHandlerVersion": "2.1",
            "location": region,
            "force_update_tag": uuid4(),
            "autoUpgradeMinorVersion": True,
            "settings": {
                "commandToExecute": to_execute_cmd,
                "fileUris": urls,
            },
            "protectedSettings": {
                "managedIdentity": {},
            },
        }
        return extension

    raise NotImplementedError("unsupported OS: %s" % vm_os)


def fuzz_extensions(pool: Pool, scaleset: Scaleset) -> List[Extension]:
    urls = [build_pool_config(pool), build_scaleset_script(pool, scaleset)]
    fuzz_extension = agent_config(scaleset.region, pool.os, AgentMode.fuzz, urls=urls)
    input_extensions = DEFAULT_EXTENSIONS
    instance_config = InstanceConfig.fetch()        
    if instance_config.extensions: 
        input_extensions.extend(instance_config.extensions)
    extensions = generic_extensions(
        scaleset.region, input_extensions, pool.os, scaleset.cert_key, scaleset.cert
    )
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
                    setup_container, StorageType.corpus, read=True, list_=True
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
    extensions = generic_extensions(region, DEFAULT_EXTENSIONS, repro_os, "", "")

    extensions += [base_extension]
    return extensions


def proxy_manager_extensions(region: Region, proxy_id: UUID) -> List[Extension]:
    urls = [
        get_file_sas_url(
            Container("proxy-configs"),
            "%s/%s/config.json" % (region, proxy_id),
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
    extensions = generic_extensions(region, DEFAULT_EXTENSIONS, OS.linux, "", "")
    extensions += [base_extension]
    return extensions
