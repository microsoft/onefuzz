import logging
import os

import azure.functions as func

from onefuzz.api import Onefuzz


def main(req: func.HttpRequest) -> func.HttpResponse:
    logging.info("Python HTTP trigger function processed a request.")

    o = Onefuzz()
    o.config(
        endpoint=os.environ.get("ONEFUZZ_ENDPOINT"),
        authority=os.environ.get("ONEFUZZ_AUTHORITY"),
        client_id=os.environ.get("ONEFUZZ_CLIENT_ID"),
        client_secret=os.environ.get("ONEFUZZ_CLIENT_SECRET"),
    )
    info = o.info.get()
    return func.HttpResponse(info.json())
