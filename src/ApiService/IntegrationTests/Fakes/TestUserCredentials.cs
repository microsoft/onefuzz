using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service;

using Async = System.Threading.Tasks;

namespace IntegrationTests.Fakes;

sealed class TestUserCredentials : UserCredentials {

    private readonly OneFuzzResult<UserInfo> _tokenResult;

    public TestUserCredentials(ILogTracer log, IConfigOperations instanceConfig, OneFuzzResult<UserInfo> tokenResult)
        : base(log, instanceConfig) {
        _tokenResult = tokenResult;
    }

    public override Task<OneFuzzResult<UserInfo>> ParseJwtToken(HttpRequestData req) => Async.Task.FromResult(_tokenResult);
}
