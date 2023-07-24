namespace FunctionalTests {
    sealed class ApiClient {
        static Microsoft.Morse.AuthenticationConfig authConfig =
                new Microsoft.Morse.AuthenticationConfig(
                    ClientId: System.Environment.GetEnvironmentVariable("ONEFUZZ_CLIENT_ID")!,
                    TenantId: System.Environment.GetEnvironmentVariable("ONEFUZZ_TENANT_ID")!,
                    Scopes: new[] { System.Environment.GetEnvironmentVariable("ONEFUZZ_SCOPES")! },
                    Secret: System.Environment.GetEnvironmentVariable("ONEFUZZ_SECRET")!);

        static Microsoft.Morse.ServiceAuth auth = new Microsoft.Morse.ServiceAuth(authConfig);
        static Microsoft.OneFuzz.Service.Request request = new Microsoft.OneFuzz.Service.Request(new HttpClient(), () => auth.Token(new CancellationToken()));

        public static Microsoft.OneFuzz.Service.Request Request => request;

        public static Uri Endpoint { get; } = new(Environment.GetEnvironmentVariable("ONEFUZZ_ENDPOINT") ?? "http://localhost:7071");
    }
}
