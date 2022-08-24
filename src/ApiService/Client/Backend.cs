using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace Microsoft.OneFuzz.Client;

class OneFuzzConfig {
    public string? ClientId { get; set; }
    public string? Authority { get; set; }
    public Uri? Endpoint { get; set; }
}

class Backend {
    private static readonly string? _homePath =
        Environment.OSVersion.Platform == PlatformID.Win32NT
        ? Environment.ExpandEnvironmentVariables("%HOMEDRIVE%%HOMEPATH%")
        : Environment.GetEnvironmentVariable("HOME");

    private const string DefaultClientId = "72f1562a-8c0c-41ea-beb9-fa2b71c80134";
    private const string DefaultAuthority = "https://login.microsoftonline.com/72f988bf-86f1-41af-91ab-2d7cd011db47";

    private readonly string _configPath;
    private readonly string _tokenPath;
    private readonly IPublicClientApplication _app;

    public OneFuzzConfig Config { get; }

    private Backend(string configPath, string tokenPath, OneFuzzConfig config, IPublicClientApplication app) {
        _configPath = configPath;
        _tokenPath = tokenPath;
        _app = app;
        Config = config;
    }

    public static async Task<Backend> Create(string? configPath = null, string? tokenPath = null) {
        var basePath = Path.Join(_homePath, ".cache", "onefuzz");
        var fullConfigPath = configPath ?? Path.Combine(basePath, "config.json");
        var fullTokenPath = tokenPath ?? Path.Combine(basePath, "access_token.json");

        var tokenCacheDir = Path.Join(basePath, "token_cache");
        Directory.CreateDirectory(tokenCacheDir);

        OneFuzzConfig config;
        try {
            var contents = await File.ReadAllBytesAsync(fullConfigPath);
            config = JsonSerializer.Deserialize<OneFuzzConfig>(contents) ?? new();
        } catch (FileNotFoundException) {
            config = new();
        }

        var tokenStorage =
            new StorageCreationPropertiesBuilder(fullTokenPath, tokenCacheDir)
            .WithUnprotectedFile()
            .Build();

        var app = PublicClientApplicationBuilder
            .Create(config.ClientId ?? DefaultClientId)
            .WithAuthority(config.Authority ?? DefaultAuthority)
            .Build();

        var cacheHelper = await MsalCacheHelper.CreateAsync(tokenStorage);
        cacheHelper.RegisterCache(app.UserTokenCache);

        return new Backend(fullConfigPath, fullTokenPath, config, app);
    }

    public OneFuzzClient CreateClient(ILogger logger) {
        var client = new HttpClient();

        if (Config.Endpoint is Uri endpoint) {
            client.BaseAddress = new Uri(endpoint, "/api/");
        } else {
            throw new InvalidOperationException("endpoint not set, use `onefuzz config --endpoint <uri>` to set it");
        }

        return new OneFuzzClient(client, _app, logger);
    }

    public async Task SaveConfig() {
        var result = JsonSerializer.Serialize(Config, new JsonSerializerOptions {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        });

        await File.WriteAllTextAsync(_configPath, result);
    }
}
