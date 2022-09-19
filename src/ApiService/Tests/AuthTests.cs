using FluentAssertions;
using Xunit;

namespace Tests {
    public class AuthTests {
        [Fact]
        public async System.Threading.Tasks.Task TestAuth() {
            var auth = await Microsoft.OneFuzz.Service.Auth.BuildAuth();

            auth.Should().NotBeNull();
            auth.PrivateKey.StartsWith("-----BEGIN OPENSSH PRIVATE KEY-----").Should().BeTrue();
            auth.PrivateKey.EndsWith("-----END OPENSSH PRIVATE KEY-----\n").Should().BeTrue();
            auth.PublicKey.StartsWith("ssh-rsa").Should().BeTrue();
        }
    }
}
