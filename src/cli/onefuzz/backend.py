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
import tempfile
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
from onefuzztypes import responses
from opentelemetry import context, propagate
from opentelemetry.instrumentation.requests import RequestsInstrumentor
from pydantic import BaseModel
from requests import Response
from tenacity import RetryCallState, retry
from tenacity.retry import retry_if_exception_type
from tenacity.stop import stop_after_attempt
from tenacity.wait import wait_random

from .__version__ import __version__
from .azcopy import azcopy_copy, azcopy_sync

_ACCESSTOKENCACHE_UMASK = 0o077

VIRTUAL_ENV = os.environ.get("VIRTUAL_ENV")
HOME_PATH = VIRTUAL_ENV if VIRTUAL_ENV else "~"
ONEFUZZ_CACHE = os.path.join(".cache", "onefuzz")
ONEFUZZ_BASE_PATH = os.path.join(HOME_PATH, ONEFUZZ_CACHE)
DEFAULT_CONFIG_PATH = os.path.join(ONEFUZZ_BASE_PATH, "config.json")
DEFAULT_TOKEN_PATH = os.path.join("~", ONEFUZZ_CACHE, "access_token.json")
REQUEST_CONNECT_TIMEOUT = 30.0
REQUEST_READ_TIMEOUT = 120.0

LOGGER = logging.getLogger("backend")
PROPAGATOR = propagate.get_global_textmap()


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
            if isinstance(as_json, dict) and "title" in as_json and "detail" in as_json:
                raise Exception(
                    f"request failed: application error (401: {as_json['title']}): {as_json['detail']}"
                )
        except json.decoder.JSONDecodeError:
            pass


class BackendConfig(BaseModel):
    authority: Optional[str]
    client_id: Optional[str]
    endpoint: str
    features: Optional[Set[str]]
    tenant_domain: Optional[str]

    def get_multi_tenant_domain(self) -> Optional[str]:
        if (
            self.authority
            and "https://login.microsoftonline.com/common" in self.authority
        ):
            return self.tenant_domain
        else:
            return None


class CacheConfig(BaseModel):
    endpoint: Optional[str]


class Backend:
    def __init__(
        self,
        config: BackendConfig,
        config_path: Optional[str] = None,
        token_path: Optional[str] = None,
        client_secret: Optional[str] = None,
    ):
        RequestsInstrumentor().instrument(skip_dep_check=True)
        self.config_path = os.path.expanduser(config_path or DEFAULT_CONFIG_PATH)
        self.token_path = os.path.expanduser(token_path or DEFAULT_TOKEN_PATH)
        self.client_secret = client_secret
        self.config = config
        self.token_cache: Optional[msal.SerializableTokenCache] = None
        self.init_cache()
        self.app: Optional[msal.ClientApplication] = None
        self.token_expires = 0
        self.load_config()
        self.session = requests.Session()

        atexit.register(self.save_cache)

    def enable_feature(self, name: str) -> None:
        if not self.config.features:
            self.config.features = Set[str]()
        self.config.features.add(name)

    def is_feature_enabled(self, name: str) -> bool:
        if self.config.features:
            return name in self.config.features
        return False

    def load_config(self) -> None:
        if os.path.exists(self.config_path):
            with open(self.config_path, "r") as handle:
                data = json.load(handle)
            self.config = BackendConfig.parse_obj(data)

    def save_config(self) -> None:
        os.makedirs(os.path.dirname(self.config_path), exist_ok=True)
        with open(self.config_path, "w") as handle:
            endpoint_cache = {"endpoint": f"{self.config.endpoint}"}
            handle.write(json.dumps(endpoint_cache, indent=4, sort_keys=True))

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
        value = {
            "Cli-Version": __version__,
        }
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

        multi_tenant_domain = self.config.get_multi_tenant_domain()
        if multi_tenant_domain is not None:
            endpoint = urlparse(self.config.endpoint).netloc.split(".")[0]
            scopes = [
                f"api://{multi_tenant_domain}/{endpoint}/.default",
            ]
        else:
            netloc = urlparse(self.config.endpoint).netloc
            scopes = [
                f"api://{netloc}/.default",
            ]

        if self.client_secret:
            return self.access_token_from_client_secret(scopes)

        return self.do_login(scopes)

    def access_token_from_client_secret(self, scopes: List[str]) -> Any:
        if not self.app:
            self.app = msal.ConfidentialClientApplication(
                self.config.client_id,
                authority=self.config.authority,
                client_credential=self.client_secret,
                token_cache=self.token_cache,
            )

        # try each scope until we successfully get an access token
        for scope in scopes:
            done, result = self.acquire_token_for_scope(self.app, scope)
            if done:
                break

        if "error" in result:
            raise Exception(
                "error: %s\n'%s'"
                % (result.get("error"), result.get("error_description"))
            )
        return result

    def acquire_token_for_scope(
        self, app: msal.ConfidentialClientApplication, scope: str
    ) -> Tuple[bool, Any]:
        # retry in the face of any connection errors
        # e.g. connection reset by peer, due to connection timeout
        retriesLeft = 5
        while True:
            try:
                result = app.acquire_token_for_client(scopes=[scope])
                if "error" not in result:
                    return (True, result)

                # AADSTS500011: The resource principal named ... was not found in the tenant named ...
                # This error is caused by a by mismatch between the identifierUrl and the scope provided in the request.
                if "AADSTS500011" in result["error_description"]:
                    LOGGER.warning(f"failed to get access token with scope {scope}")
                    return (False, result)
                else:
                    # unexpected error
                    return (True, result)
            except requests.exceptions.ConnectionError:
                retriesLeft -= 1
                if retriesLeft == 0:
                    raise

    def do_login(self, scopes: List[str]) -> Any:
        if not self.app:
            self.app = msal.PublicClientApplication(
                self.config.client_id,
                authority=self.config.authority,
                token_cache=self.token_cache,
                allow_broker=True,
            )

        access_token = None
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
            try:
                access_token = self.app.acquire_token_interactive(
                    scopes=[scope],
                    parent_window_handle=msal.PublicClientApplication.CONSOLE_WINDOW_HANDLE,
                )
                check_msal_error(access_token, ["access_token"])
            except KeyboardInterrupt:
                result = input(
                    "\nInteractive login cancelled. Use device login (Y/n)? "
                )
                if result == "" or result.startswith("y") or result.startswith("Y"):
                    print("Falling back to device flow, please sign in:", flush=True)
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
                else:
                    continue

            LOGGER.info("Interactive device authentication succeeded")
            print("Login succeeded", flush=True)
            self.save_cache()
            break

        if access_token:
            return access_token
        else:
            raise Exception("Failed to acquire token")

    def config_params(
        self,
    ) -> None:
        if self.config.endpoint is None:
            raise Exception("Endpoint Not Configured")

        endpoint = self.config.endpoint

        response = self.session.request("GET", endpoint + "/api/config")

        endpoint_params = responses.Config.parse_obj(response.json())

        # Will override values in storage w/ provided values for SP use
        if not self.config.client_id:
            self.config.client_id = endpoint_params.client_id
        if not self.config.authority:
            self.config.authority = endpoint_params.authority
        if not self.config.tenant_domain:
            self.config.tenant_domain = endpoint_params.tenant_domain

    def request(
        self,
        method: str,
        path: str,
        json_data: Optional[Any] = None,
        params: Optional[Any] = None,
        _retry_on_auth_failure: bool = True,
    ) -> Response:
        endpoint = self.config.endpoint

        if not endpoint:
            raise Exception("endpoint not configured")

        url = endpoint + "/api/" + path
        if not self.config.client_id or (
            not self.config.authority and not self.config.tenant_domain
        ):
            self.config_params()
        headers = self.headers()
        if str.lower(os.environ.get("ONEFUZZ_STRICT_VERSIONING") or "") == "true":
            headers["Strict-Version"] = "true"
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
                current_context = context.get_current()
                LOGGER.debug("request %s %s %s", method, url, repr(json_data))

                request_to_downstream = requests.Request(
                    method, url, headers=headers, json=json_data, params=params
                )

                PROPAGATOR.inject(
                    carrier=request_to_downstream.headers,
                    context=current_context,
                )

                correlation_id = ""
                for span in current_context.values():
                    correlation_id = str(hex(span.__dict__["_context"].trace_id))[2:]
                    break

                LOGGER.debug("OneFuzz CorrelationId: %s", correlation_id)
                prep_req = self.session.prepare_request(request_to_downstream)

                response = self.session.send(
                    prep_req, timeout=(REQUEST_CONNECT_TIMEOUT, REQUEST_READ_TIMEOUT)
                )

                if response.status_code not in retry_codes:
                    break

                check_application_error(response)

                LOGGER.info("request bad status code: %s", response.status_code)
            except ConnectionResetError as err:
                # in our case this means some kind of lower-level timeout was hit;
                # treat it as a retryable error
                LOGGER.info("connection reset error: %s", err)
            except requests.exceptions.ConnectionError as err:
                LOGGER.info("request connection error: %s", err)
            except requests.exceptions.ReadTimeout as err:
                LOGGER.info("request timed out: %s", err)

            time.sleep(1.5**backoff)

        if response is None:
            raise Exception("request failed: %s %s" % (method, url))

        if response.status_code // 100 != 2:
            try:
                json = response.json()
                # attempt to read as https://www.rfc-editor.org/rfc/rfc7807
                if isinstance(json, Dict):
                    title = json.get("title")
                    details = json.get("detail")
                    raise Exception(
                        f"request did not succeed ({response.status_code}: {title}): {details}"
                    )
            except requests.exceptions.JSONDecodeError:
                pass

            error_text = str(
                response.content, encoding="utf-8", errors="backslashreplace"
            )
            raise Exception(
                "request did not succeed: HTTP %s - %s"
                % (response.status_code, error_text)
            )

        return response


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
    client: ContainerClient

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
        try:
            # Split the container URL to insert the blob_name
            url_parts = self.container_url.split("?", 1)

            # Default to azcopy if it is installed
            azcopy_copy(file_path, url_parts[0] + "/" + blob_name + "?" + url_parts[1])
        except Exception as exc:
            # A subprocess exception would typically only contain the exit status.
            LOGGER.warning(
                "Upload using azcopy failed. Check the azcopy logs for more information."
            )
            LOGGER.warning(exc)
            # Indicate the switch in the approach for clarity in debugging
            LOGGER.warning("Now attempting to upload using the Python SDK...")

            # This does not have a try/except since it should be caught by the retry system.
            # The retry system will always attempt azcopy first and this approach second
            with open(file_path, "rb") as handle:
                # Using the Azure SDK default max_concurrency
                self.client.upload_blob(name=blob_name, data=handle, overwrite=True)
        return None

    def upload_file_data(self, data: str, blob_name: str) -> None:
        with tempfile.TemporaryDirectory() as tmpdir:
            filename = os.path.join(tmpdir, blob_name)

            with open(filename, "w") as handle:
                handle.write(data)

            self.upload_file(filename, blob_name)

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
