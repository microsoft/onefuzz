using FluentAssertions;
using IntegrationTests;
using Microsoft.OneFuzz.Service;
using Xunit;
using Xunit.Abstractions;

namespace Tests {

    public class AuthTests {
        protected ILogTracer Logger { get; }
        public AuthTests(ITestOutputHelper output) {
            Logger = new TestLogTracer(output);
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
