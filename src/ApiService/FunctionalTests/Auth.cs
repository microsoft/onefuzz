using Microsoft.Identity.Client;

namespace Microsoft.Morse;

public record AuthenticationConfig(string ClientId, string TenantId, string Secret, string[] Scopes);

interface IServiceAuth {
    Task<AuthenticationResult> Auth(CancellationToken cancelationToken);
}

public class ServiceAuth : IServiceAuth, IDisposable {
    private SemaphoreSlim _lockObj = new SemaphoreSlim(1);
    private AuthenticationResult? _token;
    private IConfidentialClientApplication _app;
    private AuthenticationConfig _authConfig;

    public ServiceAuth(AuthenticationConfig authConfig) {
        _authConfig = authConfig;

        _app = ConfidentialClientApplicationBuilder
                .Create(authConfig.ClientId)
                .WithClientSecret(authConfig.Secret)
                .WithTenantId(authConfig.TenantId)
                .WithLegacyCacheCompatibility(false)
                .Build();
    }

    public async Task<AuthenticationResult> Auth(CancellationToken cancelationToken) {
        await _lockObj.WaitAsync(cancelationToken);
        if (cancelationToken.IsCancellationRequested)
            throw new System.Exception("Canellation requested, aborting Auth");

        try {
            if (_token is null) {
                throw new MsalUiRequiredException(MsalError.ActivityRequired, "Authenticating for the first time");
            } else {
                var now = System.DateTimeOffset.UtcNow;
                if (_token.ExpiresOn < now) {
                    //_log.LogInformation("Cached token expired on : {token}. DateTime Offset Now: {now}", _token.ExpiresOn, now);
                    throw new MsalUiRequiredException(MsalError.ActivityRequired, "Cached token expired");
                } else {
                    return _token;
                }
            }
        } catch (MsalUiRequiredException) {
            //_log.LogInformation("Getting new token due to {msg}", ex.Message);
            _token = await _app.AcquireTokenForClient(_authConfig.Scopes).ExecuteAsync(cancelationToken);
            return _token;
        } finally {
            _ = _lockObj.Release();
        }
    }

    public void Dispose() {
        ((IDisposable)_lockObj).Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task<(string, string)> Token(CancellationToken cancellationToken) {
        var t = await Auth(cancellationToken);
        return (t.TokenType, t.AccessToken);
    }
}
