using System;
using System.Collections.Generic;
using Microsoft.Azure.Functions.Worker;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.Auth;

namespace IntegrationTests.Fakes;

public class TestFunctionContext : FunctionContext {
    public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>();

    public void SetUserAuthInfo(UserInfo userInfo)
        => this.SetUserAuthInfo(new UserAuthInfo(userInfo, new List<string>()));

    // everything else unsupported

    public override string InvocationId => throw new NotSupportedException();

    public override string FunctionId => throw new NotSupportedException();

    public override TraceContext TraceContext => throw new NotSupportedException();

    public override BindingContext BindingContext => throw new NotSupportedException();

    public override RetryContext RetryContext => throw new NotSupportedException();

    public override IServiceProvider InstanceServices { get => throw new NotSupportedException(); set => throw new NotImplementedException(); }

    public override FunctionDefinition FunctionDefinition => throw new NotSupportedException();

    public override IInvocationFeatures Features => throw new NotSupportedException();
}
