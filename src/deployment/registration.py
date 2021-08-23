#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import argparse
import logging
import time
import urllib.parse
from datetime import datetime, timedelta
from enum import Enum
from typing import Any, Callable, Dict, List, NamedTuple, Optional, Tuple, TypeVar
from uuid import UUID, uuid4

import requests
from azure.cli.core.azclierror import AuthenticationError
from azure.common.client_factory import get_client_from_cli_profile
from azure.common.credentials import get_cli_profile
from azure.graphrbac import GraphRbacManagementClient
from azure.graphrbac.models import (
    Application,
    ApplicationCreateParameters,
    ApplicationUpdateParameters,
    AppRole,
    PasswordCredential,
    RequiredResourceAccess,
    ResourceAccess,
    ServicePrincipal,
    ServicePrincipalCreateParameters,
)
from functional import seq
from msrest.serialization import TZ_UTC

FIX_URL = (
    "https://ms.portal.azure.com/#blade/Microsoft_AAD_RegisteredApps/"
    "ApplicationMenuBlade/ProtectAnAPI/appId/%s/isMSAApp/"
)

logger = logging.getLogger("deploy")


class GraphQueryError(Exception):
    def __init__(self, message: str, status_code: int) -> None:
        super(GraphQueryError, self).__init__(message)
        self.message = message
        self.status_code = status_code


def query_microsoft_graph(
    method: str,
    resource: str,
    params: Optional[Dict] = None,
    body: Optional[Dict] = None,
) -> Any:
    profile = get_cli_profile()
    (token_type, access_token, _), _, _ = profile.get_raw_token(
        resource="https://graph.microsoft.com"
    )
    url = urllib.parse.urljoin("https://graph.microsoft.com/v1.0/", resource)
    headers = {
        "Authorization": "%s %s" % (token_type, access_token),
        "Content-Type": "application/json",
    }
    response = requests.request(
        method=method, url=url, headers=headers, params=params, json=body
    )

    response.status_code

    if 200 <= response.status_code < 300:
        try:
            return response.json()
        except ValueError:
            return None
    else:
        error_text = str(response.content, encoding="utf-8", errors="backslashreplace")
        raise GraphQueryError(
            "request did not succeed: HTTP %s - %s"
            % (response.status_code, error_text),
            response.status_code,
        )


OperationResult = TypeVar("OperationResult")


def retry(
    operation: Callable[[], OperationResult],
    description: str,
    tries: int = 10,
    wait_duration: int = 10,
) -> OperationResult:
    count = 0
    while count < tries:
        count += 1
        if count > 1:
            logger.info(f"retrying '{description}'")
        try:
            return operation()
        except GraphQueryError as err:
            error = err
            # modeled after AZ-CLI's handling of missing application
            # See: https://github.com/Azure/azure-cli/blob/
            #   e015d5bcba0c2d21dc42189daa43dc1eb82d2485/src/azure-cli/
            #   azure/cli/command_modules/util/tests/
            #   latest/test_rest.py#L191-L192
            if "Request_ResourceNotFound" in repr(err):
                logger.info(f"failed '{description}' missing required resource")
            else:
                logger.warning(f"failed '{description}': {err.message}")
        time.sleep(wait_duration)
    if error:
        raise error
    else:
        raise Exception(f"failed '{description}'")


def get_graph_client(subscription_id: str) -> GraphRbacManagementClient:
    client: GraphRbacManagementClient = get_client_from_cli_profile(
        GraphRbacManagementClient,
        subscription_id=subscription_id,
    )
    return client


class ApplicationInfo(NamedTuple):
    client_id: UUID
    client_secret: str
    authority: str


class OnefuzzAppRole(Enum):
    ManagedNode = "ManagedNode"
    CliClient = "CliClient"


def register_application(
    registration_name: str,
    onefuzz_instance_name: str,
    approle: OnefuzzAppRole,
    subscription_id: str,
) -> ApplicationInfo:
    logger.info("retrieving the application registration %s" % registration_name)
    client = get_graph_client(subscription_id)
    apps: List[Application] = list(
        client.applications.list(filter="displayName eq '%s'" % registration_name)
    )

    if len(apps) == 0:
        logger.info("No existing registration found. creating a new one")
        app = create_application_registration(
            onefuzz_instance_name, registration_name, approle, subscription_id
        )
    else:
        app = apps[0]
        logger.info(
            "Found existing application objectId '%s' - appid '%s'"
            % (app.object_id, app.app_id)
        )

    onefuzz_apps: List[Application] = list(
        client.applications.list(filter="displayName eq '%s'" % onefuzz_instance_name)
    )

    if len(onefuzz_apps) == 0:
        raise Exception("onefuzz app not found")

    onefuzz_app = onefuzz_apps[0]
    pre_authorized_applications = (
        onefuzz_app.pre_authorized_applications
        if onefuzz_app.pre_authorized_applications is not None
        else []
    )

    if app.app_id not in [app.app_id for app in pre_authorized_applications]:
        authorize_application(UUID(app.app_id), UUID(onefuzz_app.app_id))

    password = create_application_credential(registration_name, subscription_id)

    return ApplicationInfo(
        client_id=app.app_id,
        client_secret=password,
        authority=("https://login.microsoftonline.com/%s" % client.config.tenant_id),
    )


def create_application_credential(application_name: str, subscription_id: str) -> str:
    """Add a new password to the application registration"""

    logger.info("creating application credential for '%s'" % application_name)
    client = get_graph_client(subscription_id)
    apps: List[Application] = list(
        client.applications.list(filter="displayName eq '%s'" % application_name)
    )

    app: Application = apps[0]

    (_, password) = add_application_password(app.object_id, subscription_id)
    return str(password)


def create_application_registration(
    onefuzz_instance_name: str, name: str, approle: OnefuzzAppRole, subscription_id: str
) -> Application:
    """Create an application registration"""

    client = get_graph_client(subscription_id)
    apps: List[Application] = list(
        client.applications.list(filter="displayName eq '%s'" % onefuzz_instance_name)
    )

    app = apps[0]
    resource_access = [
        ResourceAccess(id=role.id, type="Role")
        for role in app.app_roles
        if role.value == approle.value
    ]

    params = ApplicationCreateParameters(
        is_device_only_auth_supported=True,
        display_name=name,
        identifier_uris=[],
        password_credentials=[],
        required_resource_access=(
            [
                RequiredResourceAccess(
                    resource_access=resource_access,
                    resource_app_id=app.app_id,
                )
            ]
            if len(resource_access) > 0
            else []
        ),
    )

    registered_app: Application = client.applications.create(params)

    logger.info("creating service principal")
    service_principal_params = ServicePrincipalCreateParameters(
        account_enabled=True,
        app_role_assignment_required=False,
        service_principal_type="Application",
        app_id=registered_app.app_id,
    )

    client.service_principals.create(service_principal_params)

    atttempts = 5
    while True:
        if atttempts < 0:
            raise Exception(
                "Unable to create application registration, Please try again"
            )

        atttempts = atttempts - 1
        try:
            time.sleep(5)

            update_param = ApplicationUpdateParameters(
                reply_urls=["https://%s.azurewebsites.net" % onefuzz_instance_name]
            )
            client.applications.patch(registered_app.object_id, update_param)

            break
        except Exception:
            continue

    authorize_application(UUID(registered_app.app_id), UUID(app.app_id))
    assign_app_role(
        onefuzz_instance_name, name, subscription_id, OnefuzzAppRole.ManagedNode
    )
    return registered_app


def add_application_password(
    app_object_id: UUID, subscription_id: str
) -> Tuple[str, str]:
    def create_password() -> Tuple[str, str]:
        password = add_application_password_impl(app_object_id, subscription_id)
        logger.info("app password created")
        return password

    # Work-around the race condition where the app is created but passwords cannot
    # be created yet.
    return retry(create_password, "create password")


def add_application_password_legacy(
    app_object_id: UUID, subscription_id: str
) -> Tuple[str, str]:
    key = str(uuid4())
    password = str(uuid4())

    client = get_graph_client(subscription_id)
    password_cred = [
        PasswordCredential(
            start_date="%s" % datetime.now(TZ_UTC).strftime("%Y-%m-%dT%H:%M.%fZ"),
            end_date="%s"
            % (datetime.now(TZ_UTC) + timedelta(days=365)).strftime(
                "%Y-%m-%dT%H:%M.%fZ"
            ),
            key_id=key,
            value=password,
        )
    ]
    client.applications.update_password_credentials(app_object_id, password_cred)
    return (key, password)


def add_application_password_impl(
    app_object_id: UUID, subscription_id: str
) -> Tuple[str, str]:
    key = uuid4()
    password_request = {
        "passwordCredential": {
            "displayName": "%s" % key,
            "startDateTime": "%s" % datetime.now(TZ_UTC).strftime("%Y-%m-%dT%H:%M.%fZ"),
            "endDateTime": "%s"
            % (datetime.now(TZ_UTC) + timedelta(days=365)).strftime(
                "%Y-%m-%dT%H:%M.%fZ"
            ),
        }
    }

    try:
        password: Dict = query_microsoft_graph(
            method="POST",
            resource="applications/%s/addPassword" % app_object_id,
            body=password_request,
        )
        return (str(key), password["secretText"])
    except AuthenticationError:
        return add_application_password_legacy(app_object_id, subscription_id)


def get_application(app_id: UUID) -> Optional[Any]:
    apps: Dict = query_microsoft_graph(
        method="GET",
        resource="applications",
        params={"$filter": "appId eq '%s'" % app_id},
    )
    if len(apps["value"]) == 0:
        return None

    return apps["value"][0]


def authorize_application(
    registration_app_id: UUID,
    onefuzz_app_id: UUID,
    permissions: List[str] = ["user_impersonation"],
) -> None:
    try:
        onefuzz_app = get_application(onefuzz_app_id)
        if onefuzz_app is None:
            logger.error("Application '%s' not found", onefuzz_app_id)
            return

        scopes = seq(onefuzz_app["api"]["oauth2PermissionScopes"]).filter(
            lambda scope: scope["value"] in permissions
        )

        existing_preAuthorizedApplications = (
            seq(onefuzz_app["api"]["preAuthorizedApplications"])
            .map(
                lambda paa: seq(paa["delegatedPermissionIds"]).map(
                    lambda permission_id: (paa["appId"], permission_id)
                )
            )
            .flatten()
        )

        preAuthorizedApplications = (
            scopes.map(lambda s: (str(registration_app_id), s["id"]))
            .union(existing_preAuthorizedApplications)
            .distinct()
            .group_by_key()
            .map(lambda data: {"appId": data[0], "delegatedPermissionIds": data[1]})
        )

        onefuzz_app_id = onefuzz_app["id"]

        def add_preauthorized_app() -> None:
            query_microsoft_graph(
                method="PATCH",
                resource="applications/%s" % onefuzz_app_id,
                body={
                    "api": {
                        "preAuthorizedApplications": preAuthorizedApplications.to_list()
                    }
                },
            )

        retry(add_preauthorized_app, "authorize application")
    except AuthenticationError:
        logger.warning("*** Browse to: %s", FIX_URL % onefuzz_app_id)
        logger.warning("*** Then add the client application %s", registration_app_id)


def create_and_display_registration(
    onefuzz_instance_name: str,
    registration_name: str,
    approle: OnefuzzAppRole,
    subscription_id: str,
    *,
    display_secret: bool = False,
) -> None:
    logger.info("Updating application registration")
    application_info = register_application(
        registration_name=registration_name,
        onefuzz_instance_name=onefuzz_instance_name,
        approle=approle,
        subscription_id=subscription_id,
    )
    if display_secret:
        print("Registration complete")
        print("These generated credentials are valid for a year")
        print(f"client_id: {application_info.client_id}")
        print(f"client_secret: {application_info.client_secret}")


def update_pool_registration(onefuzz_instance_name: str, subscription_id: str) -> None:
    create_and_display_registration(
        onefuzz_instance_name,
        "%s_pool" % onefuzz_instance_name,
        OnefuzzAppRole.ManagedNode,
        subscription_id,
    )


def assign_app_role_manually(
    onefuzz_instance_name: str,
    application_name: str,
    subscription_id: str,
    app_role: OnefuzzAppRole,
) -> None:

    client = get_graph_client(subscription_id)
    apps: List[Application] = list(
        client.applications.list(filter="displayName eq '%s'" % onefuzz_instance_name)
    )

    if not apps:
        raise Exception("onefuzz app registration not found")

    app = apps[0]
    appId = app.app_id

    onefuzz_service_principals: List[ServicePrincipal] = list(
        client.service_principals.list(filter="appId eq '%s'" % appId)
    )

    if not onefuzz_service_principals:
        raise Exception("onefuzz app service principal not found")
    onefuzz_service_principal = onefuzz_service_principals[0]

    scaleset_service_principals: List[ServicePrincipal] = list(
        client.service_principals.list(filter="displayName eq '%s'" % application_name)
    )

    if not scaleset_service_principals:
        raise Exception("scaleset service principal not found")
    scaleset_service_principal = scaleset_service_principals[0]

    scaleset_service_principal.app_roles
    app_roles: List[AppRole] = [
        role for role in app.app_roles if role.value == app_role.value
    ]

    if not app_roles:
        raise Exception(
            "ManagedNode role not found in the OneFuzz application "
            "registration. Please redeploy the instance"
        )

    body = '{ "principalId": "%s", "resourceId": "%s", "appRoleId": "%s"}' % (
        scaleset_service_principal.object_id,
        onefuzz_service_principal.object_id,
        app_roles[0].id,
    )

    query = (
        "az rest --method post --url "
        "https://graph.microsoft.com/v1.0/servicePrincipals/%s/appRoleAssignedTo "
        "--body '%s' --headers \"Content-Type\"=application/json"
        % (scaleset_service_principal.object_id, body)
    )

    logger.warning(
        "execute the following query in the azure portal bash shell : \n%s" % query
    )


def assign_app_role(
    onefuzz_instance_name: str,
    application_name: str,
    subscription_id: str,
    app_role: OnefuzzAppRole,
) -> None:
    """
    Allows the application to access the service by assigning
    their managed identity to the provided App Role
    """
    try:
        onefuzz_service_appId = query_microsoft_graph(
            method="GET",
            resource="applications",
            params={
                "$filter": "displayName eq '%s'" % onefuzz_instance_name,
                "$select": "appId",
            },
        )
        if len(onefuzz_service_appId["value"]) == 0:
            raise Exception("onefuzz app registration not found")
        appId = onefuzz_service_appId["value"][0]["appId"]
        onefuzz_service_principals = query_microsoft_graph(
            method="GET",
            resource="servicePrincipals",
            params={"$filter": "appId eq '%s'" % appId},
        )

        if len(onefuzz_service_principals["value"]) == 0:
            raise Exception("onefuzz app service principal not found")
        onefuzz_service_principal = onefuzz_service_principals["value"][0]
        scaleset_service_principals = query_microsoft_graph(
            method="GET",
            resource="servicePrincipals",
            params={"$filter": "displayName eq '%s'" % application_name},
        )
        if len(scaleset_service_principals["value"]) == 0:
            raise Exception("scaleset service principal not found")
        scaleset_service_principal = scaleset_service_principals["value"][0]
        managed_node_role = (
            seq(onefuzz_service_principal["appRoles"])
            .filter(lambda x: x["value"] == app_role.value)
            .head_option()
        )

        if not managed_node_role:
            raise Exception(
                f"{app_role.value} role not found in the OneFuzz application "
                "registration. Please redeploy the instance"
            )
        assignments = query_microsoft_graph(
            method="GET",
            resource="servicePrincipals/%s/appRoleAssignments"
            % scaleset_service_principal["id"],
        )

        # check if the role is already assigned
        role_assigned = seq(assignments["value"]).find(
            lambda assignment: assignment["appRoleId"] == managed_node_role["id"]
        )
        if not role_assigned:
            query_microsoft_graph(
                method="POST",
                resource="servicePrincipals/%s/appRoleAssignedTo"
                % scaleset_service_principal["id"],
                body={
                    "principalId": scaleset_service_principal["id"],
                    "resourceId": onefuzz_service_principal["id"],
                    "appRoleId": managed_node_role["id"],
                },
            )
    except AuthenticationError:
        assign_app_role_manually(
            onefuzz_instance_name, application_name, subscription_id, app_role
        )


def set_app_audience(objectId: str, audience: str) -> None:
    # typical audience values: AzureADMyOrg, AzureADMultipleOrgs
    http_body = {"signInAudience": audience}
    try:
        query_microsoft_graph(
            method="PATCH",
            resource="applications/%s" % objectId,
            body=http_body,
        )
    except GraphQueryError:
        query = (
            "az rest --method patch --url "
            "https://graph.microsoft.com/v1.0/applications/%s "
            "--body '%s' --headers \"Content-Type\"=application/json"
            % (objectId, http_body)
        )
        logger.warning(
            "execute the following query in the azure portal bash shell and "
            "run deploy.py again : \n%s",
            query,
        )
        err_str = (
            "Unable to set signInAudience using Microsoft Graph Query API. \n"
            "The user must enable single/multi tenancy in the "
            "'Authentication' blade of the "
            "Application Registration in the "
            "AAD web portal, or use the azure bash shell "
            "using the command given above."
        )
        raise Exception(err_str)


def main() -> None:
    formatter = argparse.ArgumentDefaultsHelpFormatter

    parent_parser = argparse.ArgumentParser(add_help=False)
    parent_parser.add_argument(
        "onefuzz_instance", help="the name of the onefuzz instance"
    )
    parent_parser.add_argument("subscription_id")

    parser = argparse.ArgumentParser(
        formatter_class=formatter,
        description=(
            "Create an application registration and/or "
            "generate a password for the pool agent"
        ),
    )
    parser.add_argument("-v", "--verbose", action="store_true")

    subparsers = parser.add_subparsers(title="commands", dest="command")
    subparsers.add_parser("update_pool_registration", parents=[parent_parser])
    role_assignment_parser = subparsers.add_parser(
        "assign_scaleset_role",
        parents=[parent_parser],
    )
    role_assignment_parser.add_argument(
        "scaleset_name",
        help="the name of the scaleset",
    )
    cli_registration_parser = subparsers.add_parser(
        "create_cli_registration", parents=[parent_parser]
    )
    cli_registration_parser.add_argument(
        "--registration_name", help="the name of the cli registration"
    )

    args = parser.parse_args()
    if args.verbose:
        level = logging.DEBUG
    else:
        level = logging.WARN

    logging.basicConfig(format="%(levelname)s:%(message)s", level=level)
    logging.getLogger("deploy").setLevel(logging.INFO)

    onefuzz_instance_name = args.onefuzz_instance
    if args.command == "update_pool_registration":
        update_pool_registration(onefuzz_instance_name, args.subscription_id)
    elif args.command == "create_cli_registration":
        registration_name = args.registration_name or ("%s_cli" % onefuzz_instance_name)
        create_and_display_registration(
            onefuzz_instance_name,
            registration_name,
            OnefuzzAppRole.CliClient,
            args.subscription_id,
            display_secret=True,
        )
    elif args.command == "assign_scaleset_role":
        assign_app_role(
            onefuzz_instance_name,
            args.scaleset_name,
            args.subscription_id,
            OnefuzzAppRole.ManagedNode,
        )
    else:
        raise Exception("invalid arguments")


if __name__ == "__main__":
    main()
