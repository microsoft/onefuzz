﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Claims;
using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Options;
using Moq;

namespace Tests.Fakes;

sealed class TestHttpRequestData : HttpRequestData {
    private static readonly ObjectSerializer Serializer =
        // we must use our shared JsonSerializerOptions to be able to serialize & deserialize polymorphic types
        new JsonObjectSerializer(Microsoft.OneFuzz.Service.OneFuzzLib.Orm.EntityConverter.GetJsonSerializerOptions());

    sealed class TestServices : IServiceProvider {
        sealed class TestOptions : IOptions<WorkerOptions> {
            // WorkerOptions only has one setting: Serializer
            public WorkerOptions Value => new() { Serializer = Serializer };
        }

        static readonly IOptions<WorkerOptions> Options = new TestOptions();

        public object? GetService(Type serviceType) {
            if (serviceType == typeof(IOptions<WorkerOptions>)) {
                return Options;
            }

            return null;
        }
    }

    private static FunctionContext NewFunctionContext() {
        // mocking this out at the moment since there’s no way to create a subclass
        var mock = new Mock<FunctionContext>();
        var services = new TestServices();
        mock.SetupGet(fc => fc.InstanceServices).Returns(services);
        return mock.Object;
    }

    public static TestHttpRequestData FromJson<T>(string method, T obj)
        => new(method, Serializer.Serialize(obj));

    public TestHttpRequestData(string method, BinaryData body)
        : base(NewFunctionContext()) {
        Method = method;
        _body = body;
    }

    private readonly BinaryData _body;

    public override Stream Body => _body.ToStream();

    public override HttpHeadersCollection Headers => throw new NotImplementedException();

    public override IReadOnlyCollection<IHttpCookie> Cookies => throw new NotImplementedException();

    public override Uri Url => throw new NotImplementedException();

    public override IEnumerable<ClaimsIdentity> Identities => throw new NotImplementedException();

    public override string Method { get; }

    public override HttpResponseData CreateResponse()
        => new TestHttpResponseData(FunctionContext);
}

sealed class TestHttpResponseData : HttpResponseData {
    public TestHttpResponseData(FunctionContext functionContext)
        : base(functionContext) { }
    public override HttpStatusCode StatusCode { get; set; }
    public override HttpHeadersCollection Headers { get; set; } = new();
    public override Stream Body { get; set; } = new MemoryStream();
    public override HttpCookies Cookies => throw new NotSupportedException();
}
