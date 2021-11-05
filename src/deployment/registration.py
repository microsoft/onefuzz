#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import argparse
import logging
import re
import time
import urllib.parse
from datetime import datetime, timedelta
from enum import Enum
from typing import Any, Callable, Dict, List, NamedTuple, Optional, Tuple, TypeVar
from uuid import UUID

import requests
from azure.common.credentials import get_cli_profile
from functional import seq
from msrest.serialization import TZ_UTC

logger = logging.getLogger("deploy")

## https://docs.microsoft.com/en-us/graph/api/overview?view=graph-rest-1.0
GRAPH_RESOURCE = "https://graph.microsoft.com"
GRAPH_RESOURCE_ENDPOINT = "https://graph.microsoft.com/v1.0"


class GraphQueryError(Exception):
    def __init__(self, message: str, status_code: Optional[int]) -> None:
        super(GraphQueryError, self).__init__(message)
        self.message = message
        self.status_code = status_code


## Queries microsoft graph api and return
def query_microsoft_graph(
    method: str,
    resource: str,
    params: Optional[Dict] = None,
    body: Optional[Dict] = None,
    subscription: Optional[str] = None,
) -> Dict:
    profile = get_cli_profile()
    (token_type, access_token, _), _, _ = profile.get_raw_token(
        resource=GRAPH_RESOURCE, subscription=subscription
    )
    url = urllib.parse.urljoin(f"{GRAPH_RESOURCE_ENDPOINT}/", resource)
    headers = {
        "Authorization": "%s %s" % (token_type, access_token),
        "Content-Type": "application/json",
    }
    response = requests.request(
        method=method, url=url, headers=headers, params=params, json=body
    )
    if 200 <= response.status_code < 300:
        if response.content and response.content.strip():
            json = response.json()
            if isinstance(json, Dict):
                return json
            else:
                raise GraphQueryError(
                    f"invalid data received expected a json object: HTTP {response.status_code} - {json}",
                    response.status_code,
                )
        else:
            return {}
    else:
        error_text = str(response.content, encoding="utf-8", errors="backslashreplace")
        raise GraphQueryError(
            f"request did not succeed: HTTP {response.status_code} - {error_text}",
            response.status_code,
        )


def query_microsoft_graph_list(
    method: str,
    resource: str,
    params: Optional[Dict] = None,
    body: Optional[Dict] = None,
    subscription: Optional[str] = None,
) -> List[Any]:
    result = query_microsoft_graph(
        method,
        resource,
        params,
        body,
        subscription,
    )
    value = result.get("value")
    if isinstance(value, list):
        return value
    else:
        raise GraphQueryError("Expected data containing a list of values", None)


def get_tenant_id(subscription_id: Optional[str] = None) -> str:
    profile = get_cli_profile()
    _, _, tenant_id = profile.get_raw_token(
        resource=GRAPH_RESOURCE, subscription=subscription_id
    )
    if isinstance(tenant_id, str):
        return tenant_id
    else:
        raise Exception(
            f"unable to retrive tenant_id for subscription {subscription_id}"
        )


OperationResult = TypeVar("OperationResult")


def retry(
    operation: Callable[[Any], OperationResult],
    description: str,
    tries: int = 10,
    wait_duration: int = 10,
    data: Any = None,
) -> OperationResult:
    count = 0
    while True:
        try:
            return operation(data)
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

        count += 1
        if count >= tries:
            if error:
                raise error
            else:
                raise Exception(f"failed '{description}'")
        else:
            logger.info(
                f"waiting {wait_duration} seconds before retrying '{description}'"
            )
            time.sleep(wait_duration)


class ApplicationInfo(NamedTuple):
    client_id: UUID
    client_secret: str
    authority: str


class OnefuzzAppRole(Enum):
    ManagedNode = "ManagedNode"
    CliClient = "CliClient"
    UserAssignment = "UserAssignment"


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

    pre_authorized_applications = onefuzz_app["api"]["preAuthorizedApplications"]

    if app["appId"] not in [app["appId"] for app in pre_authorized_applications]:
        authorize_application(UUID(app["appId"]), UUID(onefuzz_app["appId"]))

    password = create_application_credential(registration_name, subscription_id)
    tenant_id = get_tenant_id(subscription_id=subscription_id)

    return ApplicationInfo(
        client_id=app["appId"],
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
        f"{application_name}_password", app["id"], subscription_id
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
        {"id": role["id"], "type": "Scope"}
        for role in app["appRoles"]
        if role["value"] == approle.value
    ]

    params = {
        "isDeviceOnlyAuthSupported": True,
        "displayName": name,
        "publicClient": {
            "redirectUris": ["https://%s.azurewebsites.net" % onefuzz_instance_name]
        },
        "isFallbackPublicClient": True,
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

    registered_app = query_microsoft_graph(
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
    assign_instance_app_role(onefuzz_instance_name, name, subscription_id, approle)
    return registered_app


def add_application_password(
    password_name: str, app_object_id: UUID, subscription_id: str
) -> Tuple[str, str]:
    def create_password(data: Any) -> Tuple[str, str]:
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

    password_request = {
        "passwordCredential": {
            "displayName": "%s" % password_name,
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
    return (password_name, password["secretText"])


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

    apps = query_microsoft_graph(
        method="GET",
        resource="applications",
        params={
            "$filter": filter_str,
        },
        subscription=subscription_id,
    )
    number_of_apps = len(apps["value"])
    if number_of_apps == 0:
        return None
    elif number_of_apps == 1:
        return apps["value"][0]
    else:
        raise Exception(
            f"Found {number_of_apps} application matching filter: '{filter_str}'"
        )


def authorize_application(
    registration_app_id: UUID,
    onefuzz_app_id: UUID,
    permissions: List[str] = ["user_impersonation"],
    subscription_id: Optional[str] = None,
) -> None:
    onefuzz_app = get_application(
        app_id=onefuzz_app_id, subscription_id=subscription_id
    )
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

    def add_preauthorized_app(app_list: List[Dict]) -> None:
        try:
            query_microsoft_graph(
                method="PATCH",
                resource="applications/%s" % onefuzz_app_id,
                body={"api": {"preAuthorizedApplications": app_list}},
                subscription=subscription_id,
            )
        except GraphQueryError as e:
            m = re.search(
                "Property PreAuthorizedApplication references "
                "applications (.*?) that cannot be found.",
                e.message,
            )
            if m:
                invalid_app_id = m.group(1)
                if invalid_app_id:
                    for app in app_list:
                        if app["appId"] == invalid_app_id:
                            logger.warning(
                                f"removing invalid id {invalid_app_id} "
                                "for the next request"
                            )
                            app_list.remove(app)

            raise e

    retry(
        add_preauthorized_app,
        "authorize application",
        data=preAuthorizedApplications.to_list(),
    )


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


def assign_app_role(
    principal_id: str,
    application_id: str,
    role_names: List[str],
    subscription_id: str,
) -> None:
    application_registrations = query_microsoft_graph_list(
        method="GET",
        resource="servicePrincipals",
        params={
            "$filter": f"appId eq '{application_id}'",
        },
        subscription=subscription_id,
    )
    if len(application_registrations) == 0:
        raise Exception(f"appid '{application_id}' was not found:")
    app = application_registrations[0]

    roles = (
        seq(app["appRoles"]).filter(lambda role: role["value"] in role_names).to_list()
    )

    if len(roles) < len(role_names):
        existing_roles = [role["value"] for role in roles]
        missing_roles = [
            role_name for role_name in role_names if role_name not in existing_roles
        ]
        raise Exception(
            f"The following roles could not be found in appId '{application_id}': {missing_roles}"
        )

    expected_role_ids = [role["id"] for role in roles]
    assignments = query_microsoft_graph_list(
        method="GET",
        resource=f"servicePrincipals/{principal_id}/appRoleAssignments",
        subscription=subscription_id,
    )
    assigned_role_ids = [assignment["appRoleId"] for assignment in assignments]
    missing_assignments = [
        id for id in expected_role_ids if id not in assigned_role_ids
    ]

    if missing_assignments:
        for app_role_id in missing_assignments:
            query_microsoft_graph(
                method="POST",
                resource=f"servicePrincipals/{principal_id}/appRoleAssignedTo",
                body={
                    "principalId": principal_id,
                    "resourceId": app["id"],
                    "appRoleId": app_role_id,
                },
                subscription=subscription_id,
            )


def assign_instance_app_role(
    onefuzz_instance_name: str,
    application_name: str,
    subscription_id: str,
    app_role: OnefuzzAppRole,
) -> None:
    """
    Allows the application to access the service by assigning
    their managed identity to the provided App Role
    """

    onefuzz_service_appIds = query_microsoft_graph_list(
        method="GET",
        resource="applications",
        params={
            "$filter": "displayName eq '%s'" % onefuzz_instance_name,
            "$select": "appId",
        },
        subscription=subscription_id,
    )
    if len(onefuzz_service_appIds) == 0:
        raise Exception("onefuzz app registration not found")
    appId = onefuzz_service_appIds[0]["appId"]
    onefuzz_service_principals = query_microsoft_graph_list(
        method="GET",
        resource="servicePrincipals",
        params={"$filter": "appId eq '%s'" % appId},
        subscription=subscription_id,
    )

    if len(onefuzz_service_principals) == 0:
        raise Exception("onefuzz app service principal not found")
    onefuzz_service_principal = onefuzz_service_principals[0]
    application_service_principals = query_microsoft_graph_list(
        method="GET",
        resource="servicePrincipals",
        params={"$filter": "displayName eq '%s'" % application_name},
        subscription=subscription_id,
    )
    if len(application_service_principals) == 0:
        raise Exception(f"application '{application_name}' service principal not found")
    application_service_principal = application_service_principals[0]
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
    assignments = query_microsoft_graph_list(
        method="GET",
        resource="servicePrincipals/%s/appRoleAssignments"
        % application_service_principal["id"],
        subscription=subscription_id,
    )

    # check if the role is already assigned
    role_assigned = seq(assignments).find(
        lambda assignment: assignment["appRoleId"] == managed_node_role["id"]
    )
    if not role_assigned:
        query_microsoft_graph(
            method="POST",
            resource="servicePrincipals/%s/appRoleAssignedTo"
            % application_service_principal["id"],
            body={
                "principalId": application_service_principal["id"],
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


def get_signed_in_user(subscription_id: Optional[str]) -> Any:
    # Get principalId by retrieving owner for SP
    try:
        app = query_microsoft_graph(
            method="GET",
            resource="me/",
            subscription=subscription_id,
        )
        return app

    except GraphQueryError:
        query = (
            "az rest --method post --url "
            "https://graph.microsoft.com/v1.0/me "
            '--headers "Content-Type"=application/json'
        )
        logger.warning(
            "execute the following query in the azure portal bash shell and "
            "run deploy.py again : \n%s",
            query,
        )
        err_str = "Unable to retrieve signed-in user via Microsoft Graph Query API. \n"
        raise Exception(err_str)


def get_service_principal(app_id: str, subscription_id: Optional[str]) -> Any:
    try:
        service_principals = query_microsoft_graph_list(
            method="GET",
            resource="servicePrincipals",
            params={"$filter": f"appId eq '{app_id}'"},
            subscription=subscription_id,
        )
        if len(service_principals) != 0:
            return service_principals[0]
        else:
            raise GraphQueryError(
                f"Could not retrieve any service principals for App Id: {app_id}", 400
            )

    except GraphQueryError:
        err_str = "Unable to add retrieve SP using Microsoft Graph Query API. \n"
        raise Exception(err_str)


def add_user(object_id: str, principal_id: str, role_id: str) -> None:
    # Get principalId by retrieving owner for SP
    # need to add users with proper role assignment
    http_body = {
        "principalId": principal_id,
        "resourceId": object_id,
        "appRoleId": role_id,
    }
    try:
        query_microsoft_graph(
            method="POST",
            resource="users/%s/appRoleAssignments" % principal_id,
            body=http_body,
        )
    except GraphQueryError as ex:
        if "Permission being assigned already exists" not in ex.message:
            query = (
                "az rest --method post --url "
                "https://graph.microsoft.com/v1.0/users/%s/appRoleAssignments "
                "--body '%s' --headers \"Content-Type\"=application/json"
                % (principal_id, http_body)
            )
            logger.warning(
                "execute the following query in the azure portal bash shell and "
                "run deploy.py again : \n%s",
                query,
            )
            err_str = "Unable to add user to SP using Microsoft Graph Query API. \n"
            raise Exception(err_str)
        else:
            logger.info("User already assigned to application.")


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
        assign_instance_app_role(
            onefuzz_instance_name,
            args.scaleset_name,
            args.subscription_id,
            OnefuzzAppRole.ManagedNode,
        )
    else:
        raise Exception("invalid arguments")


if __name__ == "__main__":
    main()
