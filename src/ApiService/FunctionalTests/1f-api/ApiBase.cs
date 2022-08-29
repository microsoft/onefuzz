using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace FunctionalTests;

abstract class ApiBase<T> where T : new() {

    Uri _endpoint;
    Microsoft.OneFuzz.Service.Request _request;
    internal ITestOutputHelper _output;

    public ApiBase(Uri endpoint, string relativeUri, Microsoft.OneFuzz.Service.Request request, ITestOutputHelper output) {
        _request = request;
        _endpoint = new Uri(endpoint, relativeUri);
        _output = output;
    }

    public abstract T Convert(JsonElement e);

    public async Task<JsonElement> Get(JsonObject root) {
        var body = root.ToJsonString();
        var r = await _request.Get(_endpoint, body);
        var ss = await r.Content.ReadAsStringAsync();
        return (await JsonDocument.ParseAsync(r.Content.ReadAsStream())).RootElement;
    }

    public async Task<JsonElement> Post(JsonObject root) {
        var body = root.ToJsonString();
        var r = await _request.Post(_endpoint, body);
        return (await JsonDocument.ParseAsync(r.Content.ReadAsStream())).RootElement;
    }

    public async Task<JsonElement> Patch(JsonObject root) {
        var body = root.ToJsonString();
        var r = await _request.Patch(_endpoint, body);
        return (await JsonDocument.ParseAsync(r.Content.ReadAsStream())).RootElement;
    }

    public async Task<JsonElement> Delete(JsonObject root) {
        var body = root.ToJsonString();
        var r = await _request.Delete(_endpoint, body);
        return (await JsonDocument.ParseAsync(r.Content.ReadAsStream())).RootElement;
    }

    public Result<IEnumerable<T>, Error> IEnumerableResult(JsonElement res) {
        if (Error.IsError(res)) {
            return Result<IEnumerable<T>, Error>.Error(new Error(res));
        } else {
            if (res.ValueKind == JsonValueKind.Array)
                return Result<IEnumerable<T>, Error>.Ok(res.EnumerateArray().Select(e => Convert(e)));
            else {
                var r = Result(res);
                if (r.IsOk)
                    return Result<IEnumerable<T>, Error>.Ok(new[] { r.OkV });
                else
                    return Result<IEnumerable<T>, Error>.Error(r.ErrorV);
            }
        }
    }
    public Result<T, Error> Result(JsonElement res) {
        if (Error.IsError(res)) {
            return Result<T, Error>.Error(new Error(res));
        } else {
            Assert.True(res.ValueKind != JsonValueKind.Array);
            return Result<T, Error>.Ok(Convert(res));
        }
    }

    public static bool DeleteResult(JsonElement res) {
        return res.GetProperty("result").GetBoolean();
    }

}
