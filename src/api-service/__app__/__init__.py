import os

import certifi.core


def override_where() -> str:
    """overrides certifi.core.where to return actual location of cacert.pem"""
    # see:
    # https://github.com/Azure/azure-functions-durable-python/issues/194#issuecomment-710670377
    # change this to match the location of cacert.pem
    return os.path.abspath(
        "cacert.pem"
    )  # or to whatever location you know contains the copy of cacert.pem


os.environ["REQUESTS_CA_BUNDLE"] = override_where()
certifi.core.where = override_where
