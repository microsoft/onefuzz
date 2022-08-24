using System;
using System.CommandLine;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensibility;

namespace Microsoft.OneFuzz.Client;

public enum OutputFormat {
    Json, Raw
}

public static class GlobalOptions {
    public static readonly Option<OutputFormat> Format = new("--format", "output format");
}

public static class EntryPoint {
    public static async Task<int> Main(string[] args) {
        var backend = await Backend.Create();

        var rootCommand = new RootCommand("test") {
            new Config(backend).Command,
            new Versions(backend).Command,
        };

        rootCommand.AddGlobalOption(GlobalOptions.Format);

        return await rootCommand.InvokeAsync(args);
    }
}

class Config {
    private readonly Backend _backend;

    public Config(Backend backend) {
        _backend = backend;
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
                    Console.Error.WriteLine("This endpoint might not be a valid OneFuzz endpoint: Missing HTTP Authentication");
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
    }
}

class Versions {
    private readonly Backend _backend;

    public Versions(Backend backend) {
        _backend = backend;
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

    async Task RunCheck(bool exact) {
        using var client = _backend.CreateClient();
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
            Console.Error.WriteLine($"incompatible versions. api: {apiStr} cli: {cliStr}");
        } else {
            Console.WriteLine("compatible");
        }
    }
}
