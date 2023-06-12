using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service;
using Xunit;
using Xunit.Abstractions;

namespace Tests {

    public class AuthTests {
        protected ILogger Logger { get; }
        public AuthTests(ITestOutputHelper output) {
            var provider = new IntegrationTests.OneFuzzLoggerProvider(output);
            Logger = provider.CreateLogger("Auth");
        }

        [Fact]
        public async System.Threading.Tasks.Task TestAuth() {
            var auth = await AuthHelpers.BuildAuth(Logger);

            auth.Should().NotBeNull();
            auth.PrivateKey.StartsWith("-----BEGIN OPENSSH PRIVATE KEY-----").Should().BeTrue();
            auth.PrivateKey.EndsWith("-----END OPENSSH PRIVATE KEY-----\n").Should().BeTrue();
            auth.PublicKey.StartsWith("ssh-rsa").Should().BeTrue();
        }
    }
}
