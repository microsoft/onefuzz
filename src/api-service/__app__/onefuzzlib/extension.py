#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import os
from typing import List, Optional
from uuid import UUID, uuid4

from onefuzztypes.enums import OS, AgentMode
from onefuzztypes.models import (
    AgentConfig,
    AzureVmExtensionConfig,
    Pool,
    ReproConfig,
    Scaleset,
)
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
from .config import InstanceConfig
from .reports import get_report


def generic_extensions(region: Region, vm_os: OS) -> List[Extension]:
    instance_config = InstanceConfig.fetch()
    extensions = []

    dependency = dependency_extension(region, vm_os)
    monitor = monitor_extension(region, vm_os)

    keyvault = None
    if instance_config.extensions.windows_keyvault and vm_os == OS.windows:
        keyvault = keyvault_extension(instance_config.extensions, vm_os)
    if instance_config.extensions.linux_keyvault and vm_os == OS.linux:
        keyvault = keyvault_extension(instance_config.extensions, vm_os)

    geneva = None
    if instance_config.extensions.geneva and vm_os == OS.windows:
        geneva = geneva_extension(instance_config.extensions)

    azmon = None
    if instance_config.extensions.azure_monitor and vm_os == OS.linux:
        azmon = azmon_extension(instance_config.extensions)

    azsec = None
    if instance_config.extensions.azure_security and vm_os == OS.linux:
        azsec = azsec_extension(instance_config.extensions)

    if dependency:
        extensions.append(dependency)
    if monitor:
        extensions.append(monitor)
    if keyvault:
        extensions.append(keyvault)
    if geneva:
        extensions.append(geneva)
    if azmon:
        extensions.append(azmon)
    if azsec:
        extensions.append(azsec)

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


def geneva_extension(extensions: AzureVmExtensionConfig) -> Extension:

    region = None

    if extensions.geneva:
        region = extensions.geneva.region

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


def azmon_extension(extensions: AzureVmExtensionConfig) -> Extension:

    location = None
    auth_id = None
    config_version = None
    moniker = None
    namespace = None
    environment = None
    account = None
    gcs_region = None
    auth_id_type = None

    if extensions.azure_monitor:
        location = extensions.azure_monitor.region
        auth_id = extensions.azure_monitor.monitoringGCSAuthId
        config_version = extensions.azure_monitor.config_version
        moniker = extensions.azure_monitor.moniker
        namespace = extensions.azure_monitor.namespace
        environment = extensions.azure_monitor.monitoringGSEnvironment
        account = extensions.azure_monitor.monitoringGCSAccount
        gcs_region = extensions.azure_monitor.monitoringGCSRegion
        auth_id_type = extensions.azure_monitor.monitoringGCSAuthIdType

    return {
        "name": "AzureMonitorLinuxAgent",
        "publisher": "Microsoft.Azure.Monitor",
        "location": location,
        "type": "AzureMonitorLinuxAgent",
        "typeHandlerVersion": "1.0",
        "autoUpgradeMinorVersion": True,
        "settings": {},
        "protectedsettings": {
            "configVersion": config_version,
            "moniker": moniker,
            "namespace": namespace,
            "monitoringGCSEnvironment": environment,
            "monitoringGCSAccount": account,
            "monitoringGCSRegion": gcs_region,
            "monitoringGCSAuthId": auth_id,
            "monitoringGCSAuthIdType": auth_id_type,
        },
    }


def azsec_extension(extensions: AzureVmExtensionConfig) -> Extension:

    region = None

    if extensions.azure_security:
        region = extensions.azure_security.region

    return {
        "name": "AzureSecurityLinuxAgent",
        "publisher": "Microsoft.Azure.Security.Monitoring",
        "location": region,
        "type": "AzureSecurityLinuxAgent",
        "typeHandlerVersion": "2.0",
        "autoUpgradeMinorVersion": True,
        "settings": {"enableGenevaUpload": True},
    }


def keyvault_extension(extensions: AzureVmExtensionConfig, vm_os: OS) -> Extension:
    # keyvault = "https://azure-policy-test-kv.vault.azure.net/secrets/"
    # cert = "Geneva-Test-Cert"
    windows_region = None
    windows_keyvault = None
    windows_cert_name = None
    windows_uri = None
    linux_region = None
    linux_keyvault = None
    linux_cert_name = None
    linux_cert_path = None
    linux_extension_store = None
    linux_uri = None
    cert_location = None

    if extensions.windows_keyvault:
        windows_region = extensions.windows_keyvault.region
        windows_keyvault = extensions.windows_keyvault.keyvault_name
        windows_cert_name = extensions.windows_keyvault.cert_name
        windows_uri = windows_keyvault + windows_cert_name

    if extensions.linux_keyvault:
        linux_region = extensions.linux_keyvault.region
        linux_keyvault = extensions.linux_keyvault.keyvault_name
        linux_cert_name = extensions.linux_keyvault.cert_name
        linux_cert_path = extensions.linux_keyvault.cert_path
        linux_extension_store = extensions.linux_keyvault.extension_store
        linux_uri = linux_keyvault + linux_cert_name
        cert_location = linux_cert_path + linux_extension_store

    if vm_os == OS.windows:
        return {
            "name": "KVVMExtensionForWindows",
            "location": windows_region,
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
                    "observedCertificates": [windows_uri],
                }
            },
        }
    elif vm_os == OS.linux:
        # cert_path = "/var/lib/waagent/"
        # extension = "Microsoft.Azure.KeyVault.Store"
        # location = cert_path + extension
        return {
            "name": "KVVMExtensionForLinux",
            "location": linux_region,
            "publisher": "Microsoft.Azure.KeyVault",
            "type": "KeyVaultForLinux",
            "typeHandlerVersion": "2.0",
            "autoUpgradeMinorVersion": True,
            "settings": {
                "secretsManagementSettings": {
                    "pollingIntervalInS": "3600",
                    "certificateStoreLocation": cert_location,
                    "observedCertificates": [linux_uri],
                },
            },
        }
    raise NotImplementedError("unsupported os: %s" % vm_os)


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

    base_extension = agent_config(
        region, repro_os, AgentMode.repro, urls=urls, with_sas=True
    )
    extensions = generic_extensions(region, repro_os)
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

    base_extension = agent_config(
        region, OS.linux, AgentMode.proxy, urls=urls, with_sas=True
    )
    extensions = generic_extensions(region, OS.linux)
    extensions += [base_extension]
    return extensions
