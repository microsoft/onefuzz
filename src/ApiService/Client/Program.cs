using System;
using System.CommandLine;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.OneFuzz.Client;

public enum OutputFormat {
    Json, Raw
}

public static class GlobalOptions {
    public static readonly Option<OutputFormat> Format = new("--format", "output format");
    public static readonly Option<bool> Verbose = new("--verbose", "verbose output");
}

public static class EntryPoint {
    public static async Task<int> Main(string[] args) {
        using var loggerFactory = LoggerFactory.Create(builder => {
            if (args.Any(x => x == "--verbose")) {
                // TODO: this should be done inside commands so they have access to parsed version
                builder.SetMinimumLevel(Extensions.Logging.LogLevel.Debug);
            }

            builder.AddSimpleConsole(options => {
                options.IncludeScopes = true;
                options.SingleLine = true;
            });

            builder.AddDebug();
        });

        var logger = loggerFactory.CreateLogger("OneFuzz");
        var backend = await Backend.Create();

        var rootCommand = new RootCommand("test") {
            new Config(backend, logger).Command,
            new Versions(backend, logger).Command,
        };

        rootCommand.AddGlobalOption(GlobalOptions.Format);
        rootCommand.AddGlobalOption(GlobalOptions.Verbose);

        return await rootCommand.InvokeAsync(args);
    }
}

class Config {
    private readonly Backend _backend;
    private readonly ILogger _logger;

    public Config(Backend backend, ILogger logger) {
        _backend = backend;
        _logger = logger;
    }

    public Command Command {
        get {
            var endpointOption = new Option<Uri?>("--endpoint", "The OneFuzz endpoint to use.");
            var clientIdOption = new Option<string?>("--client_id");
            var authorityOption = new Option<string?>("--authority");
            var cmd = new Command("config") { endpointOption, clientIdOption, authorityOption };
            cmd.SetHandler(Run, endpointOption, clientIdOption, authorityOption);
            return cmd;
        }
    }

    public async Task Run(Uri? endpoint, string? clientId, string? authority) {
        if (endpoint is not null) {
            // validate endpoint exists:
            using (var client = new HttpClient()) {
                var response = await client.GetAsync(endpoint);
                if (response.StatusCode != HttpStatusCode.Unauthorized) {
                    _logger.LogWarning("This endpoint might not be a valid OneFuzz endpoint: Missing HTTP Authentication");
                }
            }

            _backend.Config.Endpoint = endpoint;
        }

        if (clientId is not null) {
            _backend.Config.ClientId = clientId;
        }

        if (authority is not null) {
            _backend.Config.Authority = authority;
        }

        await _backend.SaveConfig();
        _logger.LogInformation("Updated OneFuzz config");
    }
}

class Versions {
    private readonly Backend _backend;
    private readonly ILogger _logger;

    public Versions(Backend backend, ILogger logger) {
        _backend = backend;
        _logger = logger;
    }

    public Command Command
        => new("versions") {
            GetCheckCommand(),
        };

    Command GetCheckCommand() {
        var exactOption = new Option<bool>("--exact");
        var checkCommand = new Command("check") { exactOption };
        checkCommand.SetHandler(RunCheck, exactOption);
        return checkCommand;
    }

    async Task<int> RunCheck(bool exact) {
        using var client = _backend.CreateClient(_logger);
        var info = await client.Invoke(Functions.Info);
        var apiStr = info.Versions["onefuzz"].Version;
        var cliStr = "3.0.0";
        bool result;
        if (exact) {
            result = apiStr == cliStr;
        } else {
            result = false;
        }

        if (!result) {
            _logger.LogError("Incompatible versions. API: {ApiVersion}, CLI: {CliVersion}", apiStr, cliStr);
            return 1;
        } else {
            _logger.LogInformation("compatible");
            return 0;
        }
    }
}
