using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service;

using Async = System.Threading.Tasks;

namespace IntegrationTests.Fakes;

sealed class TestUserCredentials : UserCredentials {

    private readonly OneFuzzResult<UserAuthInfo> _tokenResult;

    public TestUserCredentials(ILogTracer log, IConfigOperations instanceConfig, OneFuzzResult<UserInfo> tokenResult)
        : base(log, instanceConfig) {
        _tokenResult = tokenResult.IsOk ? OneFuzzResult<UserAuthInfo>.Ok(new UserAuthInfo(tokenResult.OkV, new List<string>())) : OneFuzzResult<UserAuthInfo>.Error(tokenResult.ErrorV);
    }

    public override Task<OneFuzzResult<UserAuthInfo>> ParseJwtToken(HttpRequestData req) => Async.Task.FromResult(_tokenResult);
}
