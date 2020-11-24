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
from typing import Any, Dict, List, NamedTuple, Optional, Tuple
from uuid import UUID, uuid4

import requests
from azure.common.client_factory import get_client_from_cli_profile
from azure.common.credentials import get_cli_profile
from azure.graphrbac import GraphRbacManagementClient
from azure.graphrbac.models import (
    Application,
    ApplicationCreateParameters,
    RequiredResourceAccess,
    ResourceAccess,
)
from functional import seq
from msrest.serialization import TZ_UTC

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


class ApplicationInfo(NamedTuple):
    client_id: UUID
    client_secret: str
    authority: str


class OnefuzzAppRole(Enum):
    ManagedNode = "ManagedNode"
    CliClient = "CliClient"


def register_application(
    registration_name: str, onefuzz_instance_name: str, approle: OnefuzzAppRole
) -> ApplicationInfo:
    logger.info("retrieving the application registration %s" % registration_name)
    client = get_client_from_cli_profile(GraphRbacManagementClient)
    apps: List[Application] = list(
        client.applications.list(filter="displayName eq '%s'" % registration_name)
    )

    if len(apps) == 0:
        logger.info("No existing registration found. creating a new one")
        app = create_application_registration(
            onefuzz_instance_name, registration_name, approle
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

    password = create_application_credential(registration_name)

    return ApplicationInfo(
        client_id=app.app_id,
        client_secret=password,
        authority=("https://login.microsoftonline.com/%s" % client.config.tenant_id),
    )


def create_application_credential(application_name: str) -> str:
    """ Add a new password to the application registration """

    logger.info("creating application credential for '%s'" % application_name)
    client = get_client_from_cli_profile(GraphRbacManagementClient)
    apps: List[Application] = list(
        client.applications.list(filter="displayName eq '%s'" % application_name)
    )

    app: Application = apps[0]

    (_, password) = add_application_password(app.object_id)
    return str(password)


def create_application_registration(
    onefuzz_instance_name: str, name: str, approle: OnefuzzAppRole
) -> Application:
    """ Create an application registration """

    client = get_client_from_cli_profile(GraphRbacManagementClient)
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

    atttempts = 5
    while True:
        if atttempts < 0:
            raise Exception(
                "Unable to create application registration, Please try again"
            )

        atttempts = atttempts - 1
        try:
            time.sleep(5)
            query_microsoft_graph(
                method="PATCH",
                resource="applications/%s" % registered_app.object_id,
                body={
                    "publicClient": {
                        "redirectUris": [
                            "https://%s.azurewebsites.net" % onefuzz_instance_name
                        ]
                    },
                    "isFallbackPublicClient": True,
                },
            )
            break
        except GraphQueryError as err:
            if err.status_code == 404:
                continue

    authorize_application(UUID(registered_app.app_id), UUID(app.app_id))
    return registered_app


def add_application_password(app_object_id: UUID) -> Tuple[str, str]:
    # Work-around the race condition where the app is created but passwords cannot
    # be created yet.

    error: Optional[GraphQueryError] = None
    count = 0
    tries = 10
    wait_duration = 10
    while count < tries:
        count += 1
        try:
            return add_application_password_impl(app_object_id)
        except GraphQueryError as err:
            error = err
            logging.warning("unable to create app password: %s", err.message)
        time.sleep(wait_duration)
    if error:
        raise error
    else:
        raise Exception("unable to create password")


def add_application_password_impl(app_object_id: UUID) -> Tuple[str, str]:
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
    )

    return (str(key), password["secretText"])


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
    onefuzz_app = get_application(onefuzz_app_id)
    if onefuzz_app is None:
        logger.error("Application '%s' not found" % onefuzz_app_id)
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

    query_microsoft_graph(
        method="PATCH",
        resource="applications/%s" % onefuzz_app["id"],
        body={
            "api": {"preAuthorizedApplications": preAuthorizedApplications.to_list()}
        },
    )


def create_and_display_registration(
    onefuzz_instance_name: str, registration_name: str, approle: OnefuzzAppRole
) -> None:
    logger.info("Updating application registration")
    application_info = register_application(
        registration_name=registration_name,
        onefuzz_instance_name=onefuzz_instance_name,
        approle=approle,
    )
    logger.info("Registration complete")
    logger.info("These generated credentials are valid for a year")
    logger.info("client_id: %s" % application_info.client_id)
    logger.info("client_secret: %s" % application_info.client_secret)


def update_pool_registration(onefuzz_instance_name: str) -> None:
    create_and_display_registration(
        onefuzz_instance_name,
        "%s_pool" % onefuzz_instance_name,
        OnefuzzAppRole.ManagedNode,
    )


def assign_scaleset_role(onefuzz_instance_name: str, scaleset_name: str) -> None:
    """
    Allows the nodes in the scaleset to access the service by assigning
    their managed identity to the ManagedNode Role
    """

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
        params={"$filter": "displayName eq '%s'" % scaleset_name},
    )
    if len(scaleset_service_principals["value"]) == 0:
        raise Exception("scaleset service principal not found")
    scaleset_service_principal = scaleset_service_principals["value"][0]

    managed_node_role = (
        seq(onefuzz_service_principal["appRoles"])
        .filter(lambda x: x["value"] == OnefuzzAppRole.ManagedNode.value)
        .head_option()
    )

    if not managed_node_role:
        raise Exception(
            "ManagedNode role not found in the OneFuzz application "
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


def main() -> None:
    formatter = argparse.ArgumentDefaultsHelpFormatter

    parent_parser = argparse.ArgumentParser(add_help=False)
    parent_parser.add_argument(
        "onefuzz_instance", help="the name of the onefuzz instance"
    )

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
        update_pool_registration(onefuzz_instance_name)
    elif args.command == "create_cli_registration":
        registration_name = args.registration_name or ("%s_cli" % onefuzz_instance_name)
        create_and_display_registration(
            onefuzz_instance_name, registration_name, OnefuzzAppRole.CliClient
        )
    elif args.command == "assign_scaleset_role":
        assign_scaleset_role(onefuzz_instance_name, args.scaleset_name)
    else:
        raise Exception("invalid arguments")


if __name__ == "__main__":
    main()
