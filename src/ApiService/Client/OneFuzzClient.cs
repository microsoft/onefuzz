using System;
using System.CommandLine.Binding;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace Microsoft.OneFuzz.Client;

// A wrapper around HttpClient to simplify declaring and invoking 
// OneFuzz REST API methods.
internal sealed class OneFuzzClient : IDisposable {
    private readonly HttpClient _client;
    private readonly IPublicClientApplication _app;

    public OneFuzzClient(HttpClient client, IPublicClientApplication app) {
        _client = client;
        _app = app;
    }

    private async Task<AuthenticationResult> GetAccessToken(CancellationToken cancellationToken = default) {
        Debug.Assert(_client.BaseAddress != null);

        // TODO: incomplete
        var scopes = new [] { $"api://{_client.BaseAddress.Host}/.default" };

        foreach (var account in await _app.GetAccountsAsync()) {
            var silentResult = await _app.AcquireTokenSilent(scopes, account).ExecuteAsync(cancellationToken);
            if (silentResult is not null) {
                return silentResult;
            }
        }

        Console.WriteLine("Please login:");
        var deviceResult = await _app.AcquireTokenWithDeviceCode(
            scopes,
            deviceCodeResult => {
                // Show the user the login message
                Console.WriteLine(deviceCodeResult.Message);
                return Task.CompletedTask;
            }).ExecuteAsync(cancellationToken);

        return deviceResult;
    }

    private async Task<string> AuthorizationHeader() {
        var accessToken = await GetAccessToken();
        return accessToken.CreateAuthorizationHeader();
    }

    public async Task<TResp> Invoke<TReq, TResp>(HttpFunction<TReq, TResp> func, TReq request, CancellationToken cancellationToken = default) {
        using var response = await _client.SendAsync(
            new HttpRequestMessage {
                Content = JsonContent.Create(request),
                RequestUri = func.Path,
                Method = func.Method,
                Headers = {
                    { "Authorization", await AuthorizationHeader() },
                },
            },
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<TResp>(cancellationToken: cancellationToken);
        return result!;
    }

    public Task<TResp> Invoke<TResp>(HttpFunction<None, TResp> func, CancellationToken cancellationToken = default)
        => Invoke(func, default, cancellationToken);

    public Task Invoke<TReq>(HttpFunction<TReq, None> func, TReq request, CancellationToken cancellationToken = default)
        => Invoke<TReq, None>(func, request, cancellationToken);

    public Task Invoke(HttpFunction<None, None> func, CancellationToken cancellationToken = default)
        => Invoke<None, None>(func, default, cancellationToken);

    public void Dispose() {
        _client.Dispose();
    }
}
