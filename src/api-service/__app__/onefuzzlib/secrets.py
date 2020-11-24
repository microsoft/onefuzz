# from onefuzztypes.models import SecretData
from typing import Tuple
from urllib.parse import urlparse

from azure.keyvault.secrets import KeyVaultSecret
from onefuzzlib.azure.creds import get_keyvault_client


def store_in_keyvault(
    keyvault_url: str, secret_name: str, secret_value: str
) -> KeyVaultSecret:
    keyvault_client = get_keyvault_client(keyvault_url)
    kvs: KeyVaultSecret = keyvault_client.set_secret(secret_name, secret_value)
    return kvs


def parse_secret_url(secret_url: str) -> Tuple[str, str]:
    u = urlparse(secret_url)
    vault_url = f"{u.scheme}://{u.netloc}"
    secret_name = u.path.split("/")[2]
    return (vault_url, secret_name)


def get_secret(secret_url: str) -> KeyVaultSecret:
    (vault_url, secret_name) = parse_secret_url(secret_url)
    keyvault_client = get_keyvault_client(vault_url)
    return keyvault_client.get_secret(secret_name)
