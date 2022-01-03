# ------------------------------------
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.
# ------------------------------------

# Adapt credentials from azure-identity to be compatible with SDK that needs msrestazure or azure.common.credentials
# Need msrest >= 0.6.0
# See also https://pypi.org/project/azure-identity/

# Source: https://github.com/jongio/azidext/blob/8374293bd80648f764237ddfc5f5223e7e98472b/python/azure_identity_credential_adapter.py

from typing import Any

from azure.core.pipeline import PipelineContext, PipelineRequest
from azure.core.pipeline.policies import BearerTokenCredentialPolicy
from azure.core.pipeline.transport import HttpRequest
from azure.identity import DefaultAzureCredential
from msrest.authentication import BasicTokenAuthentication


class AzureIdentityCredentialAdapter(BasicTokenAuthentication):
    def __init__(
        self,
        credential: Any = None,
        resource_id: Any = "https://management.azure.com/.default",
        **kwargs: Any
    ):
        """Adapt any azure-identity credential to work with SDK that needs azure.common.credentials or msrestazure.

        Default resource is ARM (syntax of endpoint v2)

        :param credential: Any azure-identity credential (DefaultAzureCredential by default)
        :param str resource_id: The scope to use to get the token (default ARM)
        """
        super(AzureIdentityCredentialAdapter, self).__init__(None)
        if credential is None:
            credential = DefaultAzureCredential()
        self._policy = BearerTokenCredentialPolicy(credential, resource_id, **kwargs)

    def _make_request(self) -> Any:
        return PipelineRequest(
            HttpRequest(
                "AzureIdentityCredentialAdapter",
                # changing from https://fakurl to https://contoso.com
                "https://contoso.com",
            ),
            PipelineContext(None),
        )

    def set_token(self) -> Any:
        """Ask the azure-core BearerTokenCredentialPolicy policy to get a token.

        Using the policy gives us for free the caching system of azure-core.
        We could make this code simpler by using private method, but by definition
        I can't assure they will be there forever, so mocking a fake call to the policy
        to extract the token, using 100% public API."""
        request = self._make_request()
        self._policy.on_request(request)
        # Read Authorization, and get the second part after Bearer
        token = request.http_request.headers["Authorization"].split(" ", 1)[1]
        self.token = {"access_token": token}

    def signed_session(self, session: Any = None) -> Any:
        self.set_token()
        return super(AzureIdentityCredentialAdapter, self).signed_session(session)
