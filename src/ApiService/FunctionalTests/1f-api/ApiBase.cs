using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace FunctionalTests;

interface IFromJsonElement<T> {
    T Convert(JsonElement e);
}

class BooleanResult : IFromJsonElement<BooleanResult> {
    JsonElement _e;
    public BooleanResult() { }
    public BooleanResult(JsonElement e) => _e = e;

    public bool Result => _e.GetProperty("result").GetBoolean();

    public BooleanResult Convert(JsonElement e) => new BooleanResult(e);
}

abstract class ApiBase {

    Uri _endpoint;
    Microsoft.OneFuzz.Service.Request _request;
    internal ITestOutputHelper _output;

    public ApiBase(Uri endpoint, string relativeUri, Microsoft.OneFuzz.Service.Request request, ITestOutputHelper output) {
        _request = request;
        _endpoint = new Uri(endpoint, relativeUri);
        _output = output;
    }

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

    public static Result<IEnumerable<T>, Error> IEnumerableResult<T>(JsonElement res) where T: IFromJsonElement<T>, new() {
        if (Error.IsError(res)) {
            return Result<IEnumerable<T>, Error>.Error(new Error(res));
        } else {
            if (res.ValueKind == JsonValueKind.Array)
                return Result<IEnumerable<T>, Error>.Ok(res.EnumerateArray().Select(e => (new T()).Convert(e)));
            else {
                var r = Result<T>(res);
                if (r.IsOk)
                    return Result<IEnumerable<T>, Error>.Ok(new[] { r.OkV });
                else
                    return Result<IEnumerable<T>, Error>.Error(r.ErrorV);
            }
        }
    }
    public static Result<T, Error> Result<T>(JsonElement res) where T: IFromJsonElement<T>, new(){
        if (Error.IsError(res)) {
            return Result<T, Error>.Error(new Error(res));
        } else {
            Assert.True(res.ValueKind != JsonValueKind.Array);
            return Result<T, Error>.Ok((new T()).Convert(res));
        }
    }

    public static T Return<T>(JsonElement res) where T: IFromJsonElement<T>, new() {
        return (new T()).Convert(res);
    }

}
