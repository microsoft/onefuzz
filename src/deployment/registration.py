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
from typing import Any, Callable, Dict, List, NamedTuple, Optional, Tuple, TypeVar, cast
from uuid import UUID, uuid4

import requests
from azure.cli.core.azclierror import AuthenticationError
from azure.common.credentials import get_cli_profile
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
    subscription: Optional[str] = None,
) -> Any:
    profile = get_cli_profile()
    (token_type, access_token, _), _, _ = profile.get_raw_token(
        resource="https://graph.microsoft.com", subscription=subscription
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


def get_tenant_id(subscription_id: Optional[str] = None) -> str:
    result = query_microsoft_graph(
        method="GET", resource="organization", subscription=subscription_id
    )
    return cast(str, result["value"][0]["id"])


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

    app = get_application(
        display_name=registration_name, subscription_id=subscription_id
    )

    if not app:
        logger.info("No existing registration found. creating a new one")
        app = create_application_registration(
            onefuzz_instance_name, registration_name, approle, subscription_id
        )
    else:
        logger.info(
            "Found existing application objectId '%s' - appid '%s'"
            % (app["id"], app["appId"])
        )

    onefuzz_app = get_application(
        display_name=onefuzz_instance_name, subscription_id=subscription_id
    )

    if not (onefuzz_app):
        raise Exception("onefuzz app not found")

    pre_authorized_applications = onefuzz_app["apiApplication"][
        "preAuthorizedApplications"
    ]

    if app["appId"] not in [app["appId"] for app in pre_authorized_applications]:
        authorize_application(UUID(app["appId"]), UUID(onefuzz_app["appId"]))

    password = create_application_credential(registration_name, subscription_id)
    tenant_id = get_tenant_id(subscription_id=subscription_id)

    return ApplicationInfo(
        client_id=app["id"],
        client_secret=password,
        authority=("https://login.microsoftonline.com/%s" % tenant_id),
    )


def create_application_credential(application_name: str, subscription_id: str) -> str:
    """Add a new password to the application registration"""

    logger.info("creating application credential for '%s'" % application_name)
    app = get_application(display_name=application_name)

    if not app:
        raise Exception("app not found")

    (_, password) = add_application_password(
        f"{application_name}_password", app["objectId"], subscription_id
    )
    return str(password)


def create_application_registration(
    onefuzz_instance_name: str, name: str, approle: OnefuzzAppRole, subscription_id: str
) -> Any:
    """Create an application registration"""

    app = get_application(
        display_name=onefuzz_instance_name, subscription_id=subscription_id
    )

    if not app:
        raise Exception("onefuzz app registration not found")

    resource_access = [
        {"id": "guid", "type": "string"}
        for role in app["appRoles"]
        if role["value"] == approle.value
    ]

    params = {
        "isDeviceOnlyAuthSupported": True,
        "displayName": name,
        "publicClient": {
            "redirectUris": ["https://%s.azurewebsites.net" % onefuzz_instance_name]
        },
        "requiredResourceAccess": (
            [
                {
                    "resourceAccess": resource_access,
                    "resourceAppId": app["appId"],
                }
            ]
            if len(resource_access) > 0
            else []
        ),
    }

    registered_app: Dict = query_microsoft_graph(
        method="POST",
        resource="applications",
        body=params,
        subscription=subscription_id,
    )

    logger.info("creating service principal")

    service_principal_params = {
        "accountEnabled": True,
        "appRoleAssignmentRequired": False,
        "servicePrincipalType": "Application",
        "appId": registered_app["appId"],
    }

    query_microsoft_graph(
        method="POST",
        resource="servicePrincipals",
        body=service_principal_params,
        subscription=subscription_id,
    )

    authorize_application(
        UUID(registered_app["appId"]),
        UUID(app["appId"]),
        subscription_id=subscription_id,
    )
    assign_app_role(
        onefuzz_instance_name, name, subscription_id, OnefuzzAppRole.ManagedNode
    )
    return registered_app


def add_application_password(
    password_name: str, app_object_id: UUID, subscription_id: str
) -> Tuple[str, str]:
    def create_password() -> Tuple[str, str]:
        password = add_application_password_impl(
            password_name, app_object_id, subscription_id
        )
        logger.info("app password created")
        return password

    # Work-around the race condition where the app is created but passwords cannot
    # be created yet.
    return retry(create_password, "create password")


def add_application_password_impl(
    password_name: str, app_object_id: UUID, subscription_id: str
) -> Tuple[str, str]:

    app = query_microsoft_graph(
        method="GET",
        resource="applications/%s" % app_object_id,
        subscription=subscription_id,
    )

    passwords = [
        x for x in app["passwordCredentials"] if x["displayName"] == password_name
    ]

    if len(passwords) > 0:
        key_id = passwords[0]["keyId"]
        query_microsoft_graph(
            method="POST",
            resource="applications/%s/removePassword" % app_object_id,
            body={"keyId": key_id},
            subscription=subscription_id,
        )

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

    password: Dict = query_microsoft_graph(
        method="POST",
        resource="applications/%s/addPassword" % app_object_id,
        body=password_request,
        subscription=subscription_id,
    )
    return (str(key), password["secretText"])


def get_application(
    app_id: Optional[UUID] = None,
    display_name: Optional[str] = None,
    subscription_id: Optional[str] = None,
) -> Optional[Any]:
    filters = []
    if app_id:
        filters.append("appId eq '%s'" % app_id)
    if display_name:
        filters.append("displayName eq '%s'" % display_name)

    filter_str = " and ".join(filters)

    apps: Dict = query_microsoft_graph(
        method="GET",
        resource="applications",
        params={
            "$filter": filter_str,
        },
        subscription=subscription_id,
    )
    if len(apps["value"]) == 0:
        return None

    return apps["value"][0]


def authorize_application(
    registration_app_id: UUID,
    onefuzz_app_id: UUID,
    permissions: List[str] = ["user_impersonation"],
    subscription_id: Optional[str] = None,
) -> None:
    try:
        onefuzz_app = get_application(app_id=onefuzz_app_id)
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
                subscription=subscription_id,
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
) -> None:
    logger.info("Updating application registration")
    application_info = register_application(
        registration_name=registration_name,
        onefuzz_instance_name=onefuzz_instance_name,
        approle=approle,
        subscription_id=subscription_id,
    )
    logger.info("Registration complete")
    logger.info("These generated credentials are valid for a year")
    logger.info("client_id: %s" % application_info.client_id)
    logger.info("client_secret: %s" % application_info.client_secret)


def update_pool_registration(onefuzz_instance_name: str, subscription_id: str) -> None:
    create_and_display_registration(
        onefuzz_instance_name,
        "%s_pool" % onefuzz_instance_name,
        OnefuzzAppRole.ManagedNode,
        subscription_id,
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

    onefuzz_service_appId = query_microsoft_graph(
        method="GET",
        resource="applications",
        params={
            "$filter": "displayName eq '%s'" % onefuzz_instance_name,
            "$select": "appId",
        },
        subscription=subscription_id,
    )
    if len(onefuzz_service_appId["value"]) == 0:
        raise Exception("onefuzz app registration not found")
    appId = onefuzz_service_appId["value"][0]["appId"]
    onefuzz_service_principals = query_microsoft_graph(
        method="GET",
        resource="servicePrincipals",
        params={"$filter": "appId eq '%s'" % appId},
        subscription=subscription_id,
    )

    if len(onefuzz_service_principals["value"]) == 0:
        raise Exception("onefuzz app service principal not found")
    onefuzz_service_principal = onefuzz_service_principals["value"][0]
    scaleset_service_principals = query_microsoft_graph(
        method="GET",
        resource="servicePrincipals",
        params={"$filter": "displayName eq '%s'" % application_name},
        subscription=subscription_id,
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
        subscription=subscription_id,
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
            subscription=subscription_id,
        )


def set_app_audience(
    objectId: str, audience: str, subscription_id: Optional[str] = None
) -> None:
    # typical audience values: AzureADMyOrg, AzureADMultipleOrgs
    http_body = {"signInAudience": audience}
    try:
        query_microsoft_graph(
            method="PATCH",
            resource="applications/%s" % objectId,
            body=http_body,
            subscription=subscription_id,
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
