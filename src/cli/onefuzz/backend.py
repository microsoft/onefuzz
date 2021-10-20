#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import atexit
import contextlib
import json
import logging
import os
import sys
import time
from dataclasses import asdict, is_dataclass
from enum import Enum
from typing import (
    Any,
    Callable,
    Dict,
    Generator,
    List,
    Optional,
    Set,
    Tuple,
    TypeVar,
    cast,
)
from urllib.parse import urlparse, urlunparse
from uuid import UUID

import msal
import requests
from azure.storage.blob import ContainerClient
from pydantic import BaseModel, Field
from tenacity import RetryCallState, retry
from tenacity.retry import retry_if_exception_type
from tenacity.stop import stop_after_attempt
from tenacity.wait import wait_random

from .azcopy import azcopy_sync

_ACCESSTOKENCACHE_UMASK = 0o077

ONEFUZZ_BASE_PATH = os.path.join("~", ".cache", "onefuzz")
DEFAULT_CONFIG_PATH = os.path.join(ONEFUZZ_BASE_PATH, "config.json")
DEFAULT_TOKEN_PATH = os.path.join(ONEFUZZ_BASE_PATH, "access_token.json")
REQUEST_CONNECT_TIMEOUT = 30.0
REQUEST_READ_TIMEOUT = 120.0

LOGGER = logging.getLogger("nsv-backend")


@contextlib.contextmanager
def _temporary_umask(new_umask: int) -> Generator[None, None, None]:
    prev_umask = None
    try:
        prev_umask = os.umask(new_umask)
        yield
    finally:
        if prev_umask is not None:
            os.umask(prev_umask)


def check_msal_error(value: Dict[str, Any], expected: List[str]) -> None:
    if "error" in value:
        if "error_description" in value:
            raise Exception(
                "error: %s\n%s" % (value["error"], value["error_description"])
            )

        raise Exception("error: %s" % (value["error"]))
    for entry in expected:
        if entry not in value:
            raise Exception("interactive login missing value: %s - %s" % (entry, value))


def check_application_error(response: requests.Response) -> None:
    if response.status_code == 401:
        try:
            as_json = json.loads(response.content)
            if isinstance(as_json, dict) and "code" in as_json and "errors" in as_json:
                raise Exception(
                    f"request failed: application error - {as_json['code']} {as_json['errors']}"
                )
        except json.decoder.JSONDecodeError:
            pass


class BackendConfig(BaseModel):
    authority: str
    client_id: str
    client_secret: Optional[str]
    endpoint: Optional[str]
    features: Set[str] = Field(default_factory=set)
    tenant_domain: Optional[str]


class Backend:
    def __init__(
        self,
        config: BackendConfig,
        config_path: Optional[str] = None,
        token_path: Optional[str] = None,
    ):
        self.config_path = os.path.expanduser(config_path or DEFAULT_CONFIG_PATH)
        self.token_path = os.path.expanduser(token_path or DEFAULT_TOKEN_PATH)
        self.config = config
        self.token_cache: Optional[msal.SerializableTokenCache] = None
        self.init_cache()
        self.app: Optional[Any] = None
        self.token_expires = 0
        self.load_config()
        self.session = requests.Session()

        atexit.register(self.save_cache)

    def enable_feature(self, name: str) -> None:
        self.config.features.add(name)

    def is_feature_enabled(self, name: str) -> bool:
        return name in self.config.features

    def load_config(self) -> None:
        if os.path.exists(self.config_path):
            with open(self.config_path, "r") as handle:
                data = json.load(handle)
            self.config = BackendConfig.parse_obj(data)

    def save_config(self) -> None:
        with open(self.config_path, "w") as handle:
            handle.write(self.config.json(indent=4, exclude_none=True))

    def init_cache(self) -> None:
        # Ensure the token_path directory exists
        try:
            dir_name = os.path.dirname(self.token_path)
            with _temporary_umask(_ACCESSTOKENCACHE_UMASK):
                os.makedirs(dir_name)
        except FileExistsError:
            pass

        self.token_cache = msal.SerializableTokenCache()
        if os.path.exists(self.token_path):
            with open(self.token_path, "r") as handle:
                self.token_cache.deserialize(handle.read())

    def save_cache(self) -> None:
        if self.token_cache is None:
            return

        if self.token_path is None:
            return

        with _temporary_umask(_ACCESSTOKENCACHE_UMASK):
            with open(self.token_path, "w") as handle:
                handle.write(self.token_cache.serialize())

    def logout(self) -> None:
        self.app = None
        self.token_cache = None
        if os.path.exists(self.token_path):
            os.unlink(self.token_path)

    def headers(self) -> Dict[str, str]:
        value = {}
        if self.config.client_id is not None:
            access_token = self.get_access_token()
            value["Authorization"] = "%s %s" % (
                access_token["token_type"],
                access_token["access_token"],
            )
        return value

    def get_access_token(self) -> Any:
        if not self.config.endpoint:
            raise Exception("endpoint not configured")

        if self.config.tenant_domain:
            endpoint = urlparse(self.config.endpoint).netloc.split(".")[0]
            scopes = [
                f"api://{self.config.tenant_domain}/{endpoint}/.default",
                f"https://{self.config.tenant_domain}/{endpoint}/.default",  # before 3.0.0 release
            ]
        else:
            netloc = urlparse(self.config.endpoint).netloc
            scopes = [
                f"api://{netloc}/.default",
                f"https://{netloc}/.default",  # before 3.0.0 release
            ]

        if self.config.client_secret:
            return self.client_secret(scopes)
        return self.device_login(scopes)

    def client_secret(self, scopes: List[str]) -> Any:
        if not self.app:
            self.app = msal.ConfidentialClientApplication(
                self.config.client_id,
                authority=self.config.authority,
                client_credential=self.config.client_secret,
                token_cache=self.token_cache,
            )

        # try each scope until we successfully get an access token
        for scope in scopes:
            result = self.app.acquire_token_for_client(scopes=[scope])
            if "error" not in result:
                break

            # AADSTS500011: The resource principal named ... was not found in the tenant named ...
            # This error is caused by a by mismatch between the identifierUr and the scope provided in the request.
            if "AADSTS500011" in result["error_description"]:
                LOGGER.warning(f"failed to get access token with scope {scope}")
            else:
                # unexpected error
                break

        if "error" in result:
            raise Exception(
                "error: %s\n'%s'"
                % (result.get("error"), result.get("error_description"))
            )
        return result

    def device_login(self, scopes: List[str]) -> Any:
        if not self.app:
            self.app = msal.PublicClientApplication(
                self.config.client_id,
                authority=self.config.authority,
                token_cache=self.token_cache,
            )

        for scope in scopes:
            accounts = self.app.get_accounts()
            if accounts:
                access_token = self.app.acquire_token_silent(
                    [scope], account=accounts[0]
                )
                if access_token:
                    return access_token

        for scope in scopes:
            LOGGER.info("Attempting interactive device login")
            print("Please login", flush=True)

            flow = self.app.initiate_device_flow(scopes=[scope])

            check_msal_error(flow, ["user_code", "message"])
            # setting the expiration time to allow us to retry the interactive login with a new scope
            flow["expires_at"] = int(time.time()) + 90  # 90 seconds from now
            print(flow["message"], flush=True)

            access_token = self.app.acquire_token_by_device_flow(flow)
            # AADSTS70016: OAuth 2.0 device flow error. Authorization is pending
            # this happens when the intractive login request times out. This heppens when the login
            # fails because of a scope mismatch.
            if (
                "error" in access_token
                and "AADSTS70016" in access_token["error_description"]
            ):
                LOGGER.warning(f"failed to get access token with scope {scope}")
                continue
            check_msal_error(access_token, ["access_token"])

            LOGGER.info("Interactive device authentication succeeded")
            print("Login succeeded", flush=True)
            self.save_cache()
            break

        if access_token:
            return access_token
        else:
            raise Exception("Failed to acquire token")

    def request(
        self,
        method: str,
        path: str,
        json_data: Optional[Any] = None,
        params: Optional[Any] = None,
        _retry_on_auth_failure: bool = True,
    ) -> Any:
        if not self.config.endpoint:
            raise Exception("endpoint not configured")
        url = self.config.endpoint + "/api/" + path
        headers = self.headers()
        json_data = serialize(json_data)

        # 401 errors with IDX10501: Signature validation failed occur
        # on rolling new oauth2 client secrets

        # 404 errors happen when new revisions of the functions code are rolling out
        # to the app service environment.
        # TODO: remove this once swapping deployment are in use

        # 502, 503, and 504 errors are often to Azure App issues.
        retry_codes = [401, 404, 429, 502, 503, 504]

        response = None
        for backoff in range(1, 10):
            try:
                LOGGER.debug("request %s %s %s", method, url, repr(json_data))
                response = self.session.request(
                    method,
                    url,
                    headers=headers,
                    json=json_data,
                    params=params,
                    timeout=(REQUEST_CONNECT_TIMEOUT, REQUEST_READ_TIMEOUT),
                )

                if response.status_code not in retry_codes:
                    break

                check_application_error(response)

                LOGGER.info("request bad status code: %s", response.status_code)
            except requests.exceptions.ConnectionError as err:
                LOGGER.info("request connection error: %s", err)
            except requests.exceptions.ReadTimeout as err:
                LOGGER.info("request timed out: %s", err)

            time.sleep(1.5 ** backoff)

        if response is None:
            raise Exception("request failed: %s %s" % (method, url))

        if response.status_code / 100 != 2:
            error_text = str(
                response.content, encoding="utf-8", errors="backslashreplace"
            )
            raise Exception(
                "request did not succeed: HTTP %s - %s"
                % (response.status_code, error_text)
            )
        return response.json()


def before_sleep(retry_state: RetryCallState) -> None:
    name = retry_state.fn.__name__ if retry_state.fn else "blob function"

    why: Optional[BaseException] = None
    if retry_state.outcome is not None:
        why = retry_state.outcome.exception()
    if why:
        LOGGER.warning("%s failed with %s, retrying ...", name, repr(why))
    else:
        LOGGER.warning("%s failed, retrying ...", name)


class ContainerWrapper:
    def __init__(self, container_url: str) -> None:
        self.client = ContainerClient.from_container_url(container_url)
        self.container_url = container_url

    @retry(
        stop=stop_after_attempt(10),
        wait=wait_random(min=1, max=3),
        retry=retry_if_exception_type(),
        before_sleep=before_sleep,
        reraise=True,
    )
    def upload_file(self, file_path: str, blob_name: str) -> None:
        with open(file_path, "rb") as handle:
            self.client.upload_blob(
                name=blob_name, data=handle, overwrite=True, max_concurrency=10
            )
        return None

    def upload_file_data(self, data: str, blob_name: str) -> None:
        self.client.upload_blob(
            name=blob_name, data=data, overwrite=True, max_concurrency=10
        )

    def upload_dir(self, dir_path: str) -> None:
        # security note: the src for azcopy comes from the server which is
        # trusted in this context, while the destination is provided by the
        # user
        azcopy_sync(dir_path, self.container_url)

    def download_dir(self, dir_path: str) -> None:
        # security note: the src for azcopy comes from the server which is
        # trusted in this context, while the destination is provided by the
        # user
        azcopy_sync(self.container_url, dir_path)

    @retry(
        stop=stop_after_attempt(10),
        wait=wait_random(min=1, max=3),
        retry=retry_if_exception_type(),
        before_sleep=before_sleep,
        reraise=True,
    )
    def delete_blob(self, blob_name: str) -> None:
        self.client.delete_blob(blob_name)
        return None

    @retry(
        stop=stop_after_attempt(10),
        wait=wait_random(min=1, max=3),
        retry=retry_if_exception_type(),
        before_sleep=before_sleep,
        reraise=True,
    )
    def download_blob(self, blob_name: str) -> bytes:
        return cast(bytes, self.client.download_blob(blob_name).content_as_bytes())

    @retry(
        stop=stop_after_attempt(10),
        wait=wait_random(min=1, max=3),
        retry=retry_if_exception_type(),
        before_sleep=before_sleep,
        reraise=True,
    )
    def list_blobs(self, *, name_starts_with: Optional[str] = None) -> List[str]:
        result = [
            x.name for x in self.client.list_blobs(name_starts_with=name_starts_with)
        ]
        return cast(List[str], result)


def container_file_path(container_url: str, blob_name: str) -> str:
    scheme, netloc, path, params, query, fragment = urlparse(container_url)

    blob_url = urlunparse(
        (scheme, netloc, path + "/" + blob_name, params, query, fragment)
    )

    return blob_url


def serialize(data: Any) -> Any:
    if data is None:
        return data
    if isinstance(data, BaseModel):
        return {serialize(a): serialize(b) for (a, b) in data.dict().items()}
    if isinstance(data, dict):
        return {serialize(a): serialize(b) for (a, b) in data.items()}
    if isinstance(data, list):
        return [serialize(x) for x in data]
    if isinstance(data, tuple):
        return tuple([serialize(x) for x in data])
    if isinstance(data, Enum):
        return data.name
    if isinstance(data, UUID):
        return str(data)
    if isinstance(data, (int, str)):
        return data
    if is_dataclass(data):
        return {serialize(a): serialize(b) for (a, b) in asdict(data).items()}

    raise Exception("unknown type %s" % type(data))


A = TypeVar("A")


def wait(func: Callable[[], Tuple[bool, str, A]], frequency: float = 1.0) -> A:
    """
    Wait until the provided func returns True

    Provides user feedback via a spinner if stdout is a TTY.
    """

    isatty = sys.stdout.isatty()
    frames = ["-", "\\", "|", "/"]
    waited = False
    last_message = None
    result = None

    try:
        while True:
            result = func()
            if result[0]:
                break
            message = result[1]

            if isatty:
                if last_message:
                    if last_message == message:
                        sys.stdout.write("\b" * (len(last_message) + 2))
                    else:
                        sys.stdout.write("\n")
                sys.stdout.write("%s %s" % (frames[0], message))
                sys.stdout.flush()
            elif last_message != message:
                print(message, flush=True)

            last_message = message
            waited = True
            time.sleep(frequency)
            frames.sort(key=frames[0].__eq__)
    finally:
        if waited and isatty:
            print(flush=True)

    return result[2]
