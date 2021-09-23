#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import argparse
import json
import logging
import os
import platform
import shutil
import subprocess
import sys
import tempfile
import time
import uuid
import zipfile
from datetime import datetime, timedelta
from typing import Dict, List, Optional, Tuple, Union, cast
from uuid import UUID

from azure.common.client_factory import get_client_from_cli_profile
from azure.common.credentials import get_cli_profile
from azure.cosmosdb.table.tableservice import TableService
from azure.graphrbac import GraphRbacManagementClient
from azure.graphrbac.models import (
    Application,
    ApplicationCreateParameters,
    ApplicationUpdateParameters,
    AppRole,
    GraphErrorException,
    OptionalClaims,
    RequiredResourceAccess,
    ResourceAccess,
    ServicePrincipalCreateParameters,
)
from azure.mgmt.applicationinsights import ApplicationInsightsManagementClient
from azure.mgmt.applicationinsights.models import (
    ApplicationInsightsComponentExportRequest,
)
from azure.mgmt.eventgrid import EventGridManagementClient
from azure.mgmt.eventgrid.models import (
    EventSubscription,
    EventSubscriptionFilter,
    RetryPolicy,
    StorageQueueEventSubscriptionDestination,
)
from azure.mgmt.resource import ResourceManagementClient, SubscriptionClient
from azure.mgmt.resource.resources.models import (
    Deployment,
    DeploymentMode,
    DeploymentProperties,
)
from azure.mgmt.storage import StorageManagementClient
from azure.storage.blob import (
    BlobServiceClient,
    ContainerSasPermissions,
    generate_container_sas,
)
from msrest.serialization import TZ_UTC

from data_migration import migrate
from registration import (
    OnefuzzAppRole,
    add_application_password,
    assign_app_role,
    authorize_application,
    get_graph_client,
    register_application,
    set_app_audience,
    update_pool_registration,
)
from set_admins import update_admins, update_allowed_aad_tenants

# Found by manually assigning the User.Read permission to application
# registration in the admin portal. The values are in the manifest under
# the section "requiredResourceAccess"
USER_READ_PERMISSION = "e1fe6dd8-ba31-4d61-89e7-88639da4683d"
MICROSOFT_GRAPH_APP_ID = "00000003-0000-0000-c000-000000000000"

ONEFUZZ_CLI_APP = "72f1562a-8c0c-41ea-beb9-fa2b71c80134"
ONEFUZZ_CLI_AUTHORITY = (
    "https://login.microsoftonline.com/72f988bf-86f1-41af-91ab-2d7cd011db47"
)
COMMON_AUTHORITY = "https://login.microsoftonline.com/common"
TELEMETRY_NOTICE = (
    "Telemetry collection on stats and OneFuzz failures are sent to Microsoft. "
    "To disable, delete the ONEFUZZ_TELEMETRY application setting in the "
    "Azure Functions instance"
)
AZCOPY_MISSING_ERROR = (
    "azcopy is not installed and unable to use the built-in version. "
    "Installation instructions are available at https://aka.ms/azcopy"
)
FUNC_TOOLS_ERROR = (
    "azure-functions-core-tools is not installed, "
    "install v3 using instructions: "
    "https://github.com/Azure/azure-functions-core-tools#installing"
)

logger = logging.getLogger("deploy")


def gen_guid() -> str:
    return str(uuid.uuid4())


class Client:
    def __init__(
        self,
        *,
        resource_group: str,
        location: str,
        application_name: str,
        owner: str,
        client_id: Optional[str],
        client_secret: Optional[str],
        app_zip: str,
        tools: str,
        instance_specific: str,
        third_party: str,
        arm_template: str,
        workbook_data: str,
        create_registration: bool,
        migrations: List[str],
        export_appinsights: bool,
        multi_tenant_domain: str,
        upgrade: bool,
        subscription_id: Optional[str],
        admins: List[UUID],
        allowed_aad_tenants: List[UUID],
    ):
        self.subscription_id = subscription_id
        self.resource_group = resource_group
        self.arm_template = arm_template
        self.location = location
        self.application_name = application_name
        self.owner = owner
        self.app_zip = app_zip
        self.tools = tools
        self.instance_specific = instance_specific
        self.third_party = third_party
        self.create_registration = create_registration
        self.multi_tenant_domain = multi_tenant_domain
        self.upgrade = upgrade
        self.results: Dict = {
            "client_id": client_id,
            "client_secret": client_secret,
        }
        if self.multi_tenant_domain:
            authority = COMMON_AUTHORITY
        else:
            authority = ONEFUZZ_CLI_AUTHORITY
        self.cli_config: Dict[str, Union[str, UUID]] = {
            "client_id": ONEFUZZ_CLI_APP,
            "authority": authority,
        }
        self.migrations = migrations
        self.export_appinsights = export_appinsights
        self.admins = admins
        self.allowed_aad_tenants = allowed_aad_tenants

        machine = platform.machine()
        system = platform.system()

        if system == "Linux" and machine == "x86_64":
            self.azcopy = os.path.join(self.tools, "linux", "azcopy")
            subprocess.check_output(["chmod", "+x", self.azcopy])
        elif system == "Windows" and machine == "AMD64":
            self.azcopy = os.path.join(self.tools, "win64", "azcopy.exe")
        else:
            azcopy = shutil.which("azcopy")
            if not azcopy:
                raise Exception(AZCOPY_MISSING_ERROR)
            else:
                logger.warn("unable to use built-in azcopy, using system install")
                self.azcopy = azcopy

        with open(workbook_data) as f:
            self.workbook_data = json.load(f)

    def get_subscription_id(self) -> str:
        if self.subscription_id:
            return self.subscription_id
        profile = get_cli_profile()
        self.subscription_id = cast(str, profile.get_subscription_id())
        return self.subscription_id

    def get_location_display_name(self) -> str:
        location_client = get_client_from_cli_profile(
            SubscriptionClient, subscription_id=self.get_subscription_id()
        )
        locations = location_client.subscriptions.list_locations(
            self.get_subscription_id()
        )
        for location in locations:
            if location.name == self.location:
                return cast(str, location.display_name)

        raise Exception("unknown location: %s", self.location)

    def check_region(self) -> None:
        # At the moment, this only checks are the specified providers available
        # in the selected region

        location = self.get_location_display_name()

        with open(self.arm_template, "r") as handle:
            arm = json.load(handle)

        client = get_client_from_cli_profile(
            ResourceManagementClient, subscription_id=self.get_subscription_id()
        )
        providers = {x.namespace: x for x in client.providers.list()}

        unsupported = []

        for resource in arm["resources"]:
            namespace, name = resource["type"].split("/", 1)

            # resource types are in the form of a/b/c....
            # only the top two are listed as resource types within providers
            name = "/".join(name.split("/")[:2])

            if namespace not in providers:
                unsupported.append("Unsupported provider: %s" % namespace)
                continue

            provider = providers[namespace]
            resource_types = {x.resource_type: x for x in provider.resource_types}
            if name not in resource_types:
                unsupported.append(
                    "Unsupported resource type: %s/%s" % (namespace, name)
                )
                continue

            resource_type = resource_types[name]
            if (
                location not in resource_type.locations
                and len(resource_type.locations) > 0
            ):
                unsupported.append(
                    "%s/%s is unsupported in %s" % (namespace, name, self.location)
                )

        if unsupported:
            print("The following resources required by onefuzz are not supported:")
            print("\n".join(["* " + x for x in unsupported]))
            sys.exit(1)

    def create_password(self, object_id: UUID) -> Tuple[str, str]:
        return add_application_password(object_id, self.get_subscription_id())

    def get_url(self) -> str:
        if self.multi_tenant_domain:
            return "https://%s/%s" % (
                self.multi_tenant_domain,
                self.application_name,
            )
        else:
            return "https://%s.azurewebsites.net" % self.application_name

    def get_identifier_uri(self) -> str:
        if self.multi_tenant_domain:
            return "api://%s/%s" % (
                self.multi_tenant_domain,
                self.application_name,
            )
        else:
            return "api://%s.azurewebsites.net" % self.application_name

    def setup_rbac(self) -> None:
        """
        Setup the client application for the OneFuzz instance.
        By default, Service Principals do not have access to create
        client applications in AAD.
        """
        if self.results["client_id"] and self.results["client_secret"]:
            logger.info("using existing client application")
            return

        client = get_client_from_cli_profile(
            GraphRbacManagementClient, subscription_id=self.get_subscription_id()
        )
        logger.info("checking if RBAC already exists")

        try:
            existing = list(
                client.applications.list(
                    filter="displayName eq '%s'" % self.application_name
                )
            )
        except GraphErrorException:
            logger.error("unable to query RBAC. Provide client_id and client_secret")
            sys.exit(1)

        app_roles = [
            AppRole(
                allowed_member_types=["Application"],
                display_name=OnefuzzAppRole.CliClient.value,
                id=str(uuid.uuid4()),
                is_enabled=True,
                description="Allows access from the CLI.",
                value=OnefuzzAppRole.CliClient.value,
            ),
            AppRole(
                allowed_member_types=["Application"],
                display_name=OnefuzzAppRole.ManagedNode.value,
                id=str(uuid.uuid4()),
                is_enabled=True,
                description="Allow access from a lab machine.",
                value=OnefuzzAppRole.ManagedNode.value,
            ),
        ]

        app: Optional[Application] = None

        if not existing:
            logger.info("creating Application registration")

            params = ApplicationCreateParameters(
                display_name=self.application_name,
                identifier_uris=[self.get_identifier_uri()],
                reply_urls=[self.get_url() + "/.auth/login/aad/callback"],
                optional_claims=OptionalClaims(id_token=[], access_token=[]),
                required_resource_access=[
                    RequiredResourceAccess(
                        resource_access=[
                            ResourceAccess(id=USER_READ_PERMISSION, type="Scope")
                        ],
                        resource_app_id=MICROSOFT_GRAPH_APP_ID,
                    )
                ],
                app_roles=app_roles,
            )

            app = client.applications.create(params)

            logger.info("creating service principal")
            service_principal_params = ServicePrincipalCreateParameters(
                account_enabled=True,
                app_role_assignment_required=False,
                service_principal_type="Application",
                app_id=app.app_id,
            )

            def try_sp_create() -> None:
                error: Optional[Exception] = None
                for _ in range(10):
                    try:
                        client.service_principals.create(service_principal_params)
                        return
                    except GraphErrorException as err:
                        # work around timing issue when creating service principal
                        # https://github.com/Azure/azure-cli/issues/14767
                        if (
                            "service principal being created must in the local tenant"
                            not in str(err)
                        ):
                            raise err
                    logger.warning(
                        "creating service principal failed with an error that occurs "
                        "due to AAD race conditions"
                    )
                    time.sleep(60)
                if error is None:
                    raise Exception("service principal creation failed")
                else:
                    raise error

            try_sp_create()

        else:
            app = existing[0]
            api_id = self.get_identifier_uri()
            if api_id not in app.identifier_uris:
                identifier_uris = app.identifier_uris
                identifier_uris.append(api_id)
                client.applications.patch(
                    app.object_id,
                    ApplicationUpdateParameters(identifier_uris=identifier_uris),
                )

            existing_role_values = [app_role.value for app_role in app.app_roles]
            has_missing_roles = any(
                [role.value not in existing_role_values for role in app_roles]
            )

            if has_missing_roles:
                # disabling the existing app role first to allow the update
                # this is a requirement to update the application roles
                for role in app.app_roles:
                    role.is_enabled = False

                client.applications.patch(
                    app.object_id, ApplicationUpdateParameters(app_roles=app.app_roles)
                )

                # overriding the list of app roles
                client.applications.patch(
                    app.object_id, ApplicationUpdateParameters(app_roles=app_roles)
                )

        if self.multi_tenant_domain and app.sign_in_audience == "AzureADMyOrg":
            set_app_audience(app.object_id, "AzureADMultipleOrgs")
        elif (
            not self.multi_tenant_domain
            and app.sign_in_audience == "AzureADMultipleOrgs"
        ):
            set_app_audience(app.object_id, "AzureADMyOrg")
        else:
            logger.debug("No change to App Registration signInAudence setting")

            creds = list(client.applications.list_password_credentials(app.object_id))
            client.applications.update_password_credentials(app.object_id, creds)

        (password_id, password) = self.create_password(app.object_id)

        cli_app = list(
            client.applications.list(filter="appId eq '%s'" % ONEFUZZ_CLI_APP)
        )

        if len(cli_app) == 0:
            logger.info(
                "Could not find the default CLI application under the current "
                "subscription, creating a new one"
            )
            app_info = register_application(
                "onefuzz-cli",
                self.application_name,
                OnefuzzAppRole.CliClient,
                self.get_subscription_id(),
            )
            if self.multi_tenant_domain:
                authority = COMMON_AUTHORITY
            else:
                authority = app_info.authority
            self.cli_config = {
                "client_id": app_info.client_id,
                "authority": authority,
            }

        else:
            onefuzz_cli_app = cli_app[0]
            authorize_application(uuid.UUID(onefuzz_cli_app.app_id), app.app_id)
            if self.multi_tenant_domain:
                authority = COMMON_AUTHORITY
            else:
                onefuzz_client = get_graph_client(self.get_subscription_id())
                authority = (
                    "https://login.microsoftonline.com/%s"
                    % onefuzz_client.config.tenant_id
                )
            self.cli_config = {
                "client_id": onefuzz_cli_app.app_id,
                "authority": authority,
            }

        self.results["client_id"] = app.app_id
        self.results["client_secret"] = password

    def deploy_template(self) -> None:
        logger.info("deploying arm template: %s", self.arm_template)

        with open(self.arm_template, "r") as template_handle:
            template = json.load(template_handle)

        client = get_client_from_cli_profile(
            ResourceManagementClient, subscription_id=self.get_subscription_id()
        )
        client.resource_groups.create_or_update(
            self.resource_group, {"location": self.location}
        )

        expiry = (datetime.now(TZ_UTC) + timedelta(days=365)).strftime(
            "%Y-%m-%dT%H:%M:%SZ"
        )

        app_func_audiences = [
            self.get_identifier_uri(),
            self.get_url(),
        ]
        if self.multi_tenant_domain:
            # clear the value in the Issuer Url field:
            # https://docs.microsoft.com/en-us/sharepoint/dev/spfx/use-aadhttpclient-enterpriseapi-multitenant
            app_func_issuer = ""
            multi_tenant_domain = {"value": self.multi_tenant_domain}
        else:
            tenant_oid = str(self.cli_config["authority"]).split("/")[-1]
            app_func_issuer = "https://sts.windows.net/%s/" % tenant_oid
            multi_tenant_domain = {"value": ""}

        params = {
            "app_func_audiences": {"value": app_func_audiences},
            "name": {"value": self.application_name},
            "owner": {"value": self.owner},
            "clientId": {"value": self.results["client_id"]},
            "clientSecret": {"value": self.results["client_secret"]},
            "app_func_issuer": {"value": app_func_issuer},
            "signedExpiry": {"value": expiry},
            "multi_tenant_domain": multi_tenant_domain,
            "workbookData": {"value": self.workbook_data},
        }
        deployment = Deployment(
            properties=DeploymentProperties(
                mode=DeploymentMode.incremental, template=template, parameters=params
            )
        )
        count = 0
        tries = 10
        error: Optional[Exception] = None
        while count < tries:
            count += 1

            try:
                result = client.deployments.begin_create_or_update(
                    self.resource_group, gen_guid(), deployment
                ).result()
                if result.properties.provisioning_state != "Succeeded":
                    logger.error(
                        "error deploying: %s",
                        json.dumps(result.as_dict(), indent=4, sort_keys=True),
                    )
                    sys.exit(1)
                self.results["deploy"] = result.properties.outputs
                return
            except Exception as err:
                error = err
                as_repr = repr(err)
                # Modeled after Azure-CLI.  See:
                # https://github.com/Azure/azure-cli/blob/
                #   3a2f6009cff788fde3b0170823c9129f187b2812/src/azure-cli-core/
                #   azure/cli/core/commands/arm.py#L1086
                if (
                    "PrincipalNotFound" in as_repr
                    and "does not exist in the directory" in as_repr
                ):
                    logger.info("application principal not available in AAD yet")
        if error:
            raise error
        else:
            raise Exception("unknown error deploying")

    def assign_scaleset_identity_role(self) -> None:
        if self.upgrade:
            logger.info("Upgrading: skipping assignment of the managed identity role")
            return
        logger.info("assigning the user managed identity role")
        assign_app_role(
            self.application_name,
            self.results["deploy"]["scaleset-identity"]["value"],
            self.get_subscription_id(),
            OnefuzzAppRole.ManagedNode,
        )

    def apply_migrations(self) -> None:
        name = self.results["deploy"]["func-name"]["value"]
        key = self.results["deploy"]["func-key"]["value"]
        table_service = TableService(account_name=name, account_key=key)
        migrate(table_service, self.migrations)

    def set_instance_config(self) -> None:
        name = self.results["deploy"]["func-name"]["value"]
        key = self.results["deploy"]["func-key"]["value"]
        tenant = UUID(self.results["deploy"]["tenant_id"]["value"])
        table_service = TableService(account_name=name, account_key=key)

        if self.admins:
            update_admins(table_service, self.application_name, self.admins)

        tenants = self.allowed_aad_tenants
        if tenant not in tenants:
            tenants.append(tenant)
        update_allowed_aad_tenants(table_service, self.application_name, tenants)

    def create_eventgrid(self) -> None:
        logger.info("creating eventgrid subscription")
        src_resource_id = self.results["deploy"]["fuzz-storage"]["value"]
        dst_resource_id = self.results["deploy"]["func-storage"]["value"]
        client = get_client_from_cli_profile(
            StorageManagementClient, subscription_id=self.get_subscription_id()
        )
        event_subscription_info = EventSubscription(
            destination=StorageQueueEventSubscriptionDestination(
                resource_id=dst_resource_id, queue_name="file-changes"
            ),
            filter=EventSubscriptionFilter(
                included_event_types=[
                    "Microsoft.Storage.BlobCreated",
                    "Microsoft.Storage.BlobDeleted",
                ]
            ),
            retry_policy=RetryPolicy(
                max_delivery_attempts=30,
                event_time_to_live_in_minutes=1440,
            ),
        )

        client = get_client_from_cli_profile(
            EventGridManagementClient, subscription_id=self.get_subscription_id()
        )
        result = client.event_subscriptions.begin_create_or_update(
            src_resource_id, "onefuzz1", event_subscription_info
        ).result()
        if result.provisioning_state != "Succeeded":
            raise Exception(
                "eventgrid subscription failed: %s"
                % json.dumps(result.as_dict(), indent=4, sort_keys=True),
            )

    def add_instance_id(self) -> None:
        logger.info("setting instance_id log export")

        container_name = "base-config"
        blob_name = "instance_id"
        account_name = self.results["deploy"]["func-name"]["value"]
        key = self.results["deploy"]["func-key"]["value"]
        account_url = "https://%s.blob.core.windows.net" % account_name
        client = BlobServiceClient(account_url, credential=key)
        if container_name not in [x["name"] for x in client.list_containers()]:
            client.create_container(container_name)

        blob_client = client.get_blob_client(container_name, blob_name)
        if blob_client.exists():
            logger.debug("instance_id already exists")
            instance_id = uuid.UUID(blob_client.download_blob().readall().decode())
        else:
            logger.debug("creating new instance_id")
            instance_id = uuid.uuid4()
            blob_client.upload_blob(str(instance_id))

        logger.info("instance_id: %s", instance_id)

    def add_log_export(self) -> None:
        if not self.export_appinsights:
            logger.info("not exporting appinsights")
            return

        container_name = "app-insights"

        logger.info("adding appinsight log export")
        account_name = self.results["deploy"]["func-name"]["value"]
        key = self.results["deploy"]["func-key"]["value"]
        account_url = "https://%s.blob.core.windows.net" % account_name
        client = BlobServiceClient(account_url, credential=key)
        if container_name not in [x["name"] for x in client.list_containers()]:
            client.create_container(container_name)

        expiry = datetime.utcnow() + timedelta(days=2 * 365)

        # NOTE: as this is a long-lived SAS url, it should not be logged and only
        # used in the the later-on export_configurations.create() call
        sas = generate_container_sas(
            account_name,
            container_name,
            account_key=key,
            permission=ContainerSasPermissions(write=True),
            expiry=expiry,
        )
        url = "%s/%s?%s" % (account_url, container_name, sas)

        record_types = (
            "Requests, Event, Exceptions, Metrics, PageViews, "
            "PageViewPerformance, Rdd, PerformanceCounters, Availability"
        )

        req = ApplicationInsightsComponentExportRequest(
            record_types=record_types,
            destination_type="Blob",
            is_enabled="true",
            destination_address=url,
        )

        app_insight_client = get_client_from_cli_profile(
            ApplicationInsightsManagementClient,
            subscription_id=self.get_subscription_id(),
        )

        to_delete = []
        for entry in app_insight_client.export_configurations.list(
            self.resource_group, self.application_name
        ):
            if (
                entry.storage_name == account_name
                and entry.container_name == container_name
            ):
                to_delete.append(entry.export_id)

        for export_id in to_delete:
            logger.info("replacing existing export: %s", export_id)
            app_insight_client.export_configurations.delete(
                self.resource_group, self.application_name, export_id
            )

        app_insight_client.export_configurations.create(
            self.resource_group, self.application_name, req
        )

    def upload_tools(self) -> None:
        logger.info("uploading tools from %s", self.tools)
        account_name = self.results["deploy"]["func-name"]["value"]
        key = self.results["deploy"]["func-key"]["value"]
        account_url = "https://%s.blob.core.windows.net" % account_name
        client = BlobServiceClient(account_url, credential=key)
        if "tools" not in [x["name"] for x in client.list_containers()]:
            client.create_container("tools")

        expiry = datetime.utcnow() + timedelta(minutes=30)

        sas = generate_container_sas(
            account_name,
            "tools",
            account_key=key,
            permission=ContainerSasPermissions(
                read=True, write=True, delete=True, list=True
            ),
            expiry=expiry,
        )
        url = "%s/%s?%s" % (account_url, "tools", sas)

        subprocess.check_output(
            [
                self.azcopy,
                "copy",
                os.path.join(self.tools, "*"),
                url,
                "--overwrite=true",
                "--recursive=true",
            ]
        )

        subprocess.check_output(
            [self.azcopy, "sync", self.tools, url, "--delete-destination", "true"]
        )

    def upload_instance_setup(self) -> None:
        logger.info("uploading instance-specific-setup from %s", self.instance_specific)
        account_name = self.results["deploy"]["func-name"]["value"]
        key = self.results["deploy"]["func-key"]["value"]
        account_url = "https://%s.blob.core.windows.net" % account_name
        client = BlobServiceClient(account_url, credential=key)
        if "instance-specific-setup" not in [
            x["name"] for x in client.list_containers()
        ]:
            client.create_container("instance-specific-setup")

        expiry = datetime.utcnow() + timedelta(minutes=30)

        sas = generate_container_sas(
            account_name,
            "instance-specific-setup",
            account_key=key,
            permission=ContainerSasPermissions(
                read=True, write=True, delete=True, list=True
            ),
            expiry=expiry,
        )
        url = "%s/%s?%s" % (account_url, "instance-specific-setup", sas)

        subprocess.check_output(
            [
                self.azcopy,
                "copy",
                os.path.join(self.instance_specific, "*"),
                url,
                "--overwrite=true",
                "--recursive=true",
            ]
        )

        subprocess.check_output(
            [
                self.azcopy,
                "sync",
                self.instance_specific,
                url,
                "--delete-destination",
                "true",
            ]
        )

    def upload_third_party(self) -> None:
        logger.info("uploading third-party tools from %s", self.third_party)
        account_name = self.results["deploy"]["fuzz-name"]["value"]
        key = self.results["deploy"]["fuzz-key"]["value"]
        account_url = "https://%s.blob.core.windows.net" % account_name

        client = BlobServiceClient(account_url, credential=key)
        containers = [x["name"] for x in client.list_containers()]

        for name in os.listdir(self.third_party):
            path = os.path.join(self.third_party, name)
            if not os.path.isdir(path):
                continue
            if name not in containers:
                client.create_container(name)

            expiry = datetime.utcnow() + timedelta(minutes=30)
            sas = generate_container_sas(
                account_name,
                name,
                account_key=key,
                permission=ContainerSasPermissions(
                    read=True, write=True, delete=True, list=True
                ),
                expiry=expiry,
            )
            url = "%s/%s?%s" % (account_url, name, sas)

            subprocess.check_output(
                [
                    self.azcopy,
                    "copy",
                    os.path.join(path, "*"),
                    url,
                    "--overwrite=true",
                    "--recursive=true",
                ]
            )

            subprocess.check_output(
                [self.azcopy, "sync", path, url, "--delete-destination", "true"]
            )

    def deploy_app(self) -> None:
        logger.info("deploying function app %s", self.app_zip)
        with tempfile.TemporaryDirectory() as tmpdirname:
            with zipfile.ZipFile(self.app_zip, "r") as zip_ref:
                func = shutil.which("func")
                assert func is not None

                zip_ref.extractall(tmpdirname)
                error: Optional[subprocess.CalledProcessError] = None
                max_tries = 5
                for i in range(max_tries):
                    try:
                        subprocess.check_output(
                            [
                                func,
                                "azure",
                                "functionapp",
                                "publish",
                                self.application_name,
                                "--python",
                                "--no-build",
                            ],
                            env=dict(os.environ, CLI_DEBUG="1"),
                            cwd=tmpdirname,
                        )
                        return
                    except subprocess.CalledProcessError as err:
                        error = err
                        if i + 1 < max_tries:
                            logger.debug("func failure error: %s", err)
                            logger.warning(
                                "function failed to deploy, waiting 60 "
                                "seconds and trying again"
                            )
                            time.sleep(60)
                if error is not None:
                    raise error

    def update_registration(self) -> None:
        if not self.create_registration:
            return
        update_pool_registration(self.application_name, self.get_subscription_id())

    def done(self) -> None:
        logger.info(TELEMETRY_NOTICE)

        cmd: List[str] = [
            "onefuzz",
            "config",
            "--endpoint",
            f"https://{self.application_name}.azurewebsites.net",
            "--authority",
            str(self.cli_config["authority"]),
            "--client_id",
            str(self.cli_config["client_id"]),
        ]

        if "client_secret" in self.cli_config:
            cmd += ["--client_secret", "YOUR_CLIENT_SECRET_HERE"]

        if self.multi_tenant_domain:
            cmd += ["--tenant_domain", str(self.multi_tenant_domain)]

        as_str = " ".join(cmd)

        logger.info(f"Update your CLI config via: {as_str}")


def arg_dir(arg: str) -> str:
    if not os.path.isdir(arg):
        raise argparse.ArgumentTypeError("not a directory: %s" % arg)
    return arg


def arg_file(arg: str) -> str:
    if not os.path.isfile(arg):
        raise argparse.ArgumentTypeError("not a file: %s" % arg)
    return arg


def main() -> None:
    rbac_only_states = [
        ("check_region", Client.check_region),
        ("rbac", Client.setup_rbac),
        ("arm", Client.deploy_template),
        ("assign_scaleset_identity_role", Client.assign_scaleset_identity_role),
    ]

    full_deployment_states = rbac_only_states + [
        ("apply_migrations", Client.apply_migrations),
        ("set_instance_config", Client.set_instance_config),
        ("eventgrid", Client.create_eventgrid),
        ("tools", Client.upload_tools),
        ("add_instance_id", Client.add_instance_id),
        ("instance-specific-setup", Client.upload_instance_setup),
        ("third-party", Client.upload_third_party),
        ("api", Client.deploy_app),
        ("export_appinsights", Client.add_log_export),
        ("update_registration", Client.update_registration),
    ]

    formatter = argparse.ArgumentDefaultsHelpFormatter
    parser = argparse.ArgumentParser(formatter_class=formatter)
    parser.add_argument("location")
    parser.add_argument("resource_group")
    parser.add_argument("application_name")
    parser.add_argument("owner")
    parser.add_argument(
        "--arm-template",
        type=arg_file,
        default="azuredeploy.json",
        help="(default: %(default)s)",
    )
    parser.add_argument(
        "--workbook-data",
        type=arg_file,
        default="workbook-data.json",
        help="(default: %(default)s)",
    )
    parser.add_argument(
        "--app-zip",
        type=arg_file,
        default="api-service.zip",
        help="(default: %(default)s)",
    )
    parser.add_argument(
        "--tools", type=arg_dir, default="tools", help="(default: %(default)s)"
    )
    parser.add_argument(
        "--instance_specific",
        type=arg_dir,
        default="instance-specific-setup",
        help="(default: %(default)s)",
    )
    parser.add_argument(
        "--third-party",
        type=arg_dir,
        default="third-party",
        help="(default: %(default)s)",
    )
    parser.add_argument("--client_id")
    parser.add_argument("--client_secret")
    parser.add_argument(
        "--start_at",
        default=full_deployment_states[0][0],
        choices=[x[0] for x in full_deployment_states],
        help=(
            "Debug deployments by starting at a specific state.  "
            "NOT FOR PRODUCTION USE.  (default: %(default)s)"
        ),
    )
    parser.add_argument("-v", "--verbose", action="store_true")
    parser.add_argument(
        "--create_pool_registration",
        action="store_true",
        help="Create an application registration and/or generate a "
        "password for the pool agent",
    )
    parser.add_argument(
        "--upgrade",
        action="store_true",
        help="Indicates that the instance is being upgraded",
    )
    parser.add_argument(
        "--apply_migrations",
        type=str,
        nargs="+",
        default=[],
        help="list of migration to apply to the azure table",
    )
    parser.add_argument(
        "--export_appinsights",
        action="store_true",
        help="enable appinsight log export",
    )
    parser.add_argument(
        "--multi_tenant_domain",
        type=str,
        default=None,
        help="enable multi-tenant authentication with this tenant domain",
    )
    parser.add_argument(
        "--subscription_id",
        type=str,
    )
    parser.add_argument(
        "--rbac_only",
        action="store_true",
        help="execute only the steps required to create the rbac resources",
    )
    parser.add_argument(
        "--set_admins",
        type=UUID,
        nargs="*",
        help="set the list of administrators (by OID in AAD)",
    )
    parser.add_argument(
        "--allowed_aad_tenants",
        type=UUID,
        nargs="*",
        help="Set additional AAD tenants beyond the tenant the app is deployed in",
    )

    args = parser.parse_args()

    if shutil.which("func") is None:
        logger.error(FUNC_TOOLS_ERROR)
        sys.exit(1)

    client = Client(
        resource_group=args.resource_group,
        location=args.location,
        application_name=args.application_name,
        owner=args.owner,
        client_id=args.client_id,
        client_secret=args.client_secret,
        app_zip=args.app_zip,
        tools=args.tools,
        instance_specific=args.instance_specific,
        third_party=args.third_party,
        arm_template=args.arm_template,
        workbook_data=args.workbook_data,
        create_registration=args.create_pool_registration,
        migrations=args.apply_migrations,
        export_appinsights=args.export_appinsights,
        multi_tenant_domain=args.multi_tenant_domain,
        upgrade=args.upgrade,
        subscription_id=args.subscription_id,
        admins=args.set_admins,
        allowed_aad_tenants=args.allowed_aad_tenants or [],
    )
    if args.verbose:
        level = logging.DEBUG
    else:
        level = logging.WARN

    logging.basicConfig(level=level)

    logging.getLogger("deploy").setLevel(logging.INFO)

    if args.rbac_only:
        logger.warning(
            "'rbac_only' specified. The deployment will execute "
            "only the steps required to create the rbac resources"
        )
        states = rbac_only_states
    else:
        states = full_deployment_states

    if args.start_at != states[0][0]:
        logger.warning(
            "*** Starting at a non-standard deployment state.  "
            "This may result in a broken deployment.  ***"
        )

    started = False
    for state in states:
        if args.start_at == state[0]:
            started = True
        if started:
            state[1](client)

    client.done()


if __name__ == "__main__":
    main()
