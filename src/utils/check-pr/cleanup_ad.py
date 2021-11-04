#!/usr/bin/env python

# IMPORTANT: Same as check-pr.py must be run from a Linux shell

import argparse
import json
import subprocess

from azure.core.credentials import AccessToken
from msgraph.core import GraphClient


class AzCliMsGraphAuth(object):
    def get_token(self, *scopes, **kwargs) -> AccessToken:
        json_token = subprocess.check_output(
            [
                "az",
                "account",
                "get-access-token",
                "--resource-type",
                "ms-graph",
                "--output",
                "json",
            ],
        )
        token = json.loads(json_token)
        self._token = AccessToken(
            token=f'{token["accessToken"]}', expires_on=token["expiresOn"]
        )
        return self._token


# Cleanup user owned App Registrations by deleting AppRegistrations
# that have 'contains' string in their name
def delete_current_user_app_registrations(contains: str) -> None:
    if not contains:
        raise Exception("Contains string must be set to a valid string")

    cred = AzCliMsGraphAuth()
    client = GraphClient(credential=cred)

    result = client.get("/me")
    result = client.get(f'/users/{result.json()["id"]}/ownedObjects')

    my_apps = []

    for x in result.json()["value"]:
        if (
            x["@odata.type"] == "#microsoft.graph.application"
            and contains in x["displayName"]
        ):
            my_apps.append((x["displayName"], x["id"]))

    for (name, id) in my_apps:
        print("Deleting: %s (%s)" % (name, id))
        result = client.delete(f"/applications/{id}")
        if not result.ok:
            print("Failed to delete: %s (%s) due to : %s" % (name, id, result.reason))

        result = client.get(f"/directory/deletedItems/{id}")
        if result.ok:
            deleted_app = result.json()
            if deleted_app["id"] == id:
                result = client.delete("/directory/deleteditems/%s" % id)
                if result.ok:
                    print("Permanently deleted: %s (%s)" % (name, id))
                else:
                    print(
                        "Failed to permanently delete: %s (%s) due to : %s"
                        % (name, id, result.reason)
                    )


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--contains", default="pr-check")
    args = parser.parse_args()
    delete_current_user_app_registrations(args.contains)


if __name__ == "__main__":
    main()
