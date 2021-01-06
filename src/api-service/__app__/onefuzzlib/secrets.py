#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.


from typing import Tuple, cast
from urllib.parse import urlparse
from uuid import uuid4

from azure.keyvault.secrets import KeyVaultSecret
from onefuzztypes.models import SecretAddress, SecretData
from pydantic import BaseModel

from .azure.creds import get_instance_name, get_keyvault_client


def save_to_keyvault(secret_data: SecretData) -> None:
    if isinstance(secret_data.secret, SecretAddress):
        return

    secret_name = str(uuid4())
    if isinstance(secret_data.secret, str):
        secret_value = secret_data.secret
    elif isinstance(secret_data.secret, BaseModel):
        secret_value = secret_data.secret.json()
    else:
        raise Exception("invalid secret data")

    kv = store_in_keyvault(get_keyvault_address(), secret_name, secret_value)
    secret_data.secret = SecretAddress(url=kv.id)


def get_secret_string_value(self: SecretData[str]) -> str:
    if isinstance(self.secret, SecretAddress):
        secret = get_secret(self.secret.url).value
        return cast(str, secret.value)
    else:
        return self.secret


def get_keyvault_address() -> str:
    # https://docs.microsoft.com/en-us/azure/key-vault/general/about-keys-secrets-certificates#vault-name-and-object-name
    return f"https://{get_instance_name()}-vault.vault.azure.net"


def store_in_keyvault(
    keyvault_url: str, secret_name: str, secret_value: str
) -> KeyVaultSecret:
    keyvault_client = get_keyvault_client(keyvault_url)
    kvs: KeyVaultSecret = keyvault_client.set_secret(secret_name, secret_value)
    return kvs


def parse_secret_url(secret_url: str) -> Tuple[str, str]:
    # format: https://{vault-name}.vault.azure.net/secrets/{secret-name}/{version}
    u = urlparse(secret_url)
    vault_url = f"{u.scheme}://{u.netloc}"
    secret_name = u.path.split("/")[2]
    return (vault_url, secret_name)


def get_secret(secret_url: str) -> KeyVaultSecret:
    (vault_url, secret_name) = parse_secret_url(secret_url)
    keyvault_client = get_keyvault_client(vault_url)
    return keyvault_client.get_secret(secret_name)


def delete_secret(secret_url: str) -> None:
    (vault_url, secret_name) = parse_secret_url(secret_url)
    keyvault_client = get_keyvault_client(vault_url)
    keyvault_client.begin_delete_secret(secret_name).wait()
