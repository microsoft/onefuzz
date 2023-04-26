import logging
import os

import azure.functions as func

from onefuzz.api import Onefuzz


def main(req: func.HttpRequest) -> func.HttpResponse:
    logging.info("Python HTTP trigger function processed a request.")

    o = Onefuzz()
    o.config(
        endpoint=os.environ.get("ONEFUZZ_ENDPOINT"),
    )
    info = o.info.get()
    return func.HttpResponse(info.json())
