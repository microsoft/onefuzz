#!/usr/bin/env python

import argparse
from azure.common.client_factory import get_client_from_cli_profile
from azure.graphrbac import GraphRbacManagementClient

# Cleanup user owned App Registrations by deleting AppRegistrations
# that have 'contains' string in their name
def delete_current_user_app_registrations(contains) :
    if not contains:
        raise Exception("Contains string must be set to a valid string")

    client = get_client_from_cli_profile(GraphRbacManagementClient)
    my_objs = client.signed_in_user.list_owned_objects()

    for o in my_objs:
        if contains in o.display_name:
            try:
                if client.applications.get(o.object_id):
                    print("Deleting: %s with object id: %s" % (o.display_name, o.object_id))
                    client.applications.delete(o.object_id)
            except:
                pass

    for x in client.deleted_applications.list():
        if contains in x.display_name:
            try:
                print("Hard deleting: %s with object id: %s" % (x.dispaly_name, x.object_id))
                client.deleted_applications.hard_delete(x.object_id)
            except:
                pass

def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--contains", default="pr-check")
    args = parser.parse_args()
    delete_current_user_app_registrations(args.contains)

if __name__ == "__main__":
    main()
