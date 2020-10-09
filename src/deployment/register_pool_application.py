#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import argparse
import json
import logging
import os
from datetime import datetime, timedelta
from typing import Dict, List, NamedTuple, Optional, Tuple
from uuid import UUID, uuid4

from azure.cli.core import get_default_cli  # type: ignore
from azure.common.client_factory import get_client_from_cli_profile
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


def az_cli(args):
    cli = get_default_cli()
    cli.invoke(args, out_file=open(os.devnull, "w"))
    if cli.result.result:
        return cli.result.result
    elif cli.result.error:
        raise cli.result.error


class ApplicationInfo(NamedTuple):
    client_id: UUID
    client_secret: str
    authority: str


def register_application(
    registration_name: str, onefuzz_instance_name: str
) -> ApplicationInfo:
    logger.debug("retrieving the application registration")
    client = get_client_from_cli_profile(GraphRbacManagementClient)
    apps: List[Application] = list(
        client.applications.list(filter="displayName eq '%s'" % registration_name)
    )

    if len(apps) == 0:
        logger.debug("No existing registration found. creating a new one")
        app = create_application_registration(onefuzz_instance_name, registration_name)
    else:
        app = apps[0]

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

    client = get_client_from_cli_profile(GraphRbacManagementClient)
    apps: List[Application] = list(
        client.applications.list(filter="displayName eq '%s'" % application_name)
    )

    app: Application = apps[0]

    (key, password) = add_application_password(app.object_id)
    return str(password)


def create_application_registration(
    onefuzz_instance_name: str, name: str
) -> Application:
    """ Create an application registration """

    client = get_client_from_cli_profile(GraphRbacManagementClient)
    apps: List[Application] = list(
        client.applications.list(filter="displayName eq '%s'" % onefuzz_instance_name)
    )

    app = apps[0]
    resource_access = [
        ResourceAccess(id=role.id, type="Role") for role in app.app_roles
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
    body = {
        "publicClient": {
            "redirectUris": ["https://%s.azurewebsites.net" % onefuzz_instance_name]
        },
        "isFallbackPublicClient": True,
    }
    az_cli(
        [
            "rest",
            "-m",
            "PATCH",
            "-u",
            "https://graph.microsoft.com/v1.0/applications/%s"
            % registered_app.object_id,
            "--headers",
            "Content-Type=application/json",
            "-b",
            json.dumps(body),
        ]
    )
    authorize_application(UUID(registered_app.app_id), UUID(app.app_id))
    return registered_app


def add_application_password(app_object_id: UUID) -> Tuple[str, str]:

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

    password: Dict = az_cli(
        [
            "rest",
            "-m",
            "POST",
            "-u",
            "https://graph.microsoft.com/v1.0/applications/%s/addPassword"
            % app_object_id,
            "-b",
            json.dumps(password_request),
        ]
    )

    return (str(key), password["secretText"])


def get_application(app_id: UUID) -> Optional[Dict]:
    apps: Dict = az_cli(
        [
            "rest",
            "-m",
            "GET",
            "-u",
            "https://graph.microsoft.com/v1.0/applications",
            "--uri-parameters",
            "$filter=appId eq '%s'" % app_id,
        ]
    )

    if len(apps["value"]) == 0:
        return None

    return apps["value"][0]


def authorize_application(
    registration_app_id: UUID,
    onefuzz_app_id: UUID,
    permissions: List[str] = ["user_impersonation"],
):
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

    body = {"api": {"preAuthorizedApplications": preAuthorizedApplications.to_list()}}

    az_cli(
        [
            "rest",
            "-m",
            "PATCH",
            "-u",
            "https://graph.microsoft.com/v1.0/applications/%s" % onefuzz_app["id"],
            "--headers",
            "Content-Type=application/json",
            "-b",
            json.dumps(body),
        ]
    )


def update_registration(application_name: str):

    logger.info("Updating application registration")
    application_info = register_application(
        registration_name=("%s_pool" % application_name),
        onefuzz_instance_name=application_name,
    )
    logger.info("Registration complete")
    logger.info("These generated credentials are valid for a year")
    logger.info("client_id: %s" % application_info.client_id)
    logger.info("client_secret: %s" % application_info.client_secret)


def main():
    formatter = argparse.ArgumentDefaultsHelpFormatter
    parser = argparse.ArgumentParser(
        formatter_class=formatter,
        description=(
            "Create an application registration and/or "
            "generate a password for the pool agent"
        ),
    )
    parser.add_argument("application_name")
    parser.add_argument("-v", "--verbose", action="store_true")
    args = parser.parse_args()
    if args.verbose:
        level = logging.DEBUG
    else:
        level = logging.WARN

    logging.basicConfig(format="%(levelname)s:%(message)s", level=level)
    logging.getLogger("deploy").setLevel(logging.INFO)

    update_registration(args.application_name)


if __name__ == "__main__":
    main()
