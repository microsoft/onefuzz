#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import argparse
import os
import sys
import time

from azure.devops.connection import Connection
from azure.devops.credentials import BasicAuthentication, BasicTokenAuthentication
from azure.devops.v6_0.work_item_tracking.models import Wiql


def main() -> None:
    parser = argparse.ArgumentParser(
        formatter_class=argparse.ArgumentDefaultsHelpFormatter
    )
    parser.add_argument(
        "--url",
        default="https://dev.azure.com/your-instance-name",
        help="ADO Instance URL",
    )
    parser.add_argument("--areapath", default="OneFuzz-Test-Project", help="areapath")
    parser.add_argument("--title", help="work item title")
    parser.add_argument(
        "--expected", type=int, help="expected number of work items", default=1
    )
    group = parser.add_mutually_exclusive_group()
    group.add_argument("--pat", default=os.environ.get("ADO_PAT"), help="ADO PAT")
    group.add_argument(
        "--token",
        default=os.environ.get("SYSTEM_ACCESSTOKEN"),
        help="ADO system access token",
    )
    args = parser.parse_args()

    if args.pat:
        creds = BasicAuthentication("PAT", args.pat)
    elif args.token:
        creds = BasicTokenAuthentication(token={"access_token": args.token})
    else:
        print("either --pat or --token is required")
        sys.exit(1)

    connection = Connection(base_url=args.url, creds=creds)
    client = connection.clients_v6_0.get_work_item_tracking_client()

    query_items = ["[System.AreaPath] = '%s'" % args.areapath]
    if args.title:
        query_items.append("[System.Title] = '%s'" % args.title)

    # Build an SQL-like query (WIQL - Work Item Query Language) using user
    # provided args to a user provided ADO instance.  In CICD, this ends up
    # unconditionally trusting system generated reports.
    query = "select [System.Id] from WorkItems where " + " AND ".join(  # nosec
        query_items
    )

    work_items = []
    for _ in range(60):
        work_items = client.query_by_wiql(Wiql(query=query)).work_items
        if len(work_items) >= args.expected:
            break
        time.sleep(2)
        print("trying again", flush=True)

    assert (
        len(work_items) >= args.expected
    ), "unexpected work items (got %d, expected at least %d)" % (
        len(work_items),
        args.expected,
    )


if __name__ == "__main__":
    main()
