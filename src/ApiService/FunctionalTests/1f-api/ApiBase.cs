using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace FunctionalTests;

public static class JsonObjectExt {
    public static JsonObject AddIfNotNullV<T>(this JsonObject o, string name, T? v) {
        if (v is not null)
            o.Add(name, JsonValue.Create(v));
        return o;
    }

    public static JsonObject AddIfNotNullEnumerableV<T>(this JsonObject o, string name, IEnumerable<T>? v) {
        if (v is not null)
            o.Add(name, JsonValue.Create(new JsonArray(v.Select(s => JsonValue.Create(s)).ToArray())));
        return o;
    }


    public static JsonObject AddV<T>(this JsonObject o, string name, T v) {
        o.Add(name, JsonValue.Create(v));
        return o;
    }
}

public static class JsonElementExt {
    public static string GetRawTextProperty(this JsonElement e, string property) {
        return e.GetProperty(property).GetRawText();
    }

    public static DateTimeOffset GetDateTimeOffsetProperty(this JsonElement e, string property) {
        return e.GetProperty(property).GetDateTimeOffset()!;
    }

    public static DateTimeOffset? GetNullableDateTimeOffsetProperty(this JsonElement e, string property) {
        return e.GetProperty(property).GetDateTimeOffset();
    }

    public static Guid? GetNullableGuidProperty(this JsonElement e, string property) {
        return e.GetProperty(property).ValueKind == JsonValueKind.Null ? null : e.GetProperty(property).GetGuid();
    }

    public static Guid GetGuidProperty(this JsonElement e, string property) {
        return e.GetProperty(property).GetGuid();
    }

    public static bool? GetNullableBoolProperty(this JsonElement e, string property) {
        return e.GetProperty(property).ValueKind == JsonValueKind.Null ? null : e.GetProperty(property).GetBoolean();
    }

    public static long? GetNullableLongProperty(this JsonElement e, string property) {
        return e.GetProperty(property).ValueKind == JsonValueKind.Null ? null : e.GetProperty(property).GetInt64();
    }

    public static string? GetNullableStringProperty(this JsonElement e, string property) {
        return e.GetProperty(property).ValueKind == JsonValueKind.Null ? null : e.GetProperty(property).GetString();
    }

    public static string GetStringProperty(this JsonElement e, string property) {
        return e.GetProperty(property).GetString()!;
    }

    public static long GetLongProperty(this JsonElement e, string property) {
        return e.GetProperty(property).GetInt64();
    }

    public static int GetIntProperty(this JsonElement e, string property) {
        return e.GetProperty(property).GetInt32();
    }

    public static bool GetBoolProperty(this JsonElement e, string property) {
        return e.GetProperty(property).GetBoolean();
    }

    public static T GetObjectProperty<T>(this JsonElement e, string property) where T : IFromJsonElement<T> {
        return T.Convert(e.GetProperty(property));
    }

    public static T? GetNullableObjectProperty<T>(this JsonElement e, string property) where T : IFromJsonElement<T> {
        return e.GetProperty(property).ValueKind == JsonValueKind.Null ? default : T.Convert(e.GetProperty(property));
    }

    public static IDictionary<string, string>? GetNullableStringDictProperty(this JsonElement e, string property) {
        return e.GetProperty(property).Deserialize<IDictionary<string, string>>();
    }

    public static IDictionary<string, string> GetStringDictProperty(this JsonElement e, string property) {
        return e.GetProperty(property).Deserialize<IDictionary<string, string>>()!;
    }

    public static IDictionary<string, T> GetDictProperty<T>(this JsonElement e, string property) where T : IFromJsonElement<T> {
        return new Dictionary<string, T>(
            e.GetProperty(property)!.Deserialize<IDictionary<string, JsonElement>>()!.Select(
                kv => KeyValuePair.Create(kv.Key, T.Convert(kv.Value))
            )
        );
    }

    public static IEnumerable<Guid> GetEnumerableGuidProperty(this JsonElement e, string property) {
        return e.GetProperty(property)!.EnumerateArray().Select(e => e.GetGuid()!);
    }


    public static IEnumerable<string> GetEnumerableStringProperty(this JsonElement e, string property) {
        return e.GetProperty(property)!.EnumerateArray().Select(e => e.GetString()!);
    }

    public static IEnumerable<string>? GetEnumerableNullableStringProperty(this JsonElement e, string property) {
        if (e.GetProperty(property).ValueKind == JsonValueKind.Null)
            return null;
        else
            return e.GetProperty(property)!.EnumerateArray().Select(e => e.GetString()!);
    }


    public static IEnumerable<T> GetEnumerableProperty<T>(this JsonElement e, string property) where T : IFromJsonElement<T> {
        return e.GetProperty(property).EnumerateArray().Select(T.Convert);
    }

    public static IEnumerable<T>? GetEnumerableNullableProperty<T>(this JsonElement e, string property) where T : IFromJsonElement<T> {
        if (e.GetProperty(property).ValueKind == JsonValueKind.Null)
            return null;
        else
            return e.GetProperty(property).EnumerateArray().Select(T.Convert);
    }

}


public interface IFromJsonElement<T> {
    static abstract T Convert(JsonElement e);
}

public class BooleanResult : IFromJsonElement<BooleanResult> {
    readonly JsonElement _e;

    public BooleanResult(JsonElement e) => _e = e;

    public bool IsError => Error.IsError(_e);

    public Error? Error => new(_e);

    public bool Result => _e.GetProperty("result").GetBoolean();

    public static BooleanResult Convert(JsonElement e) => new(e);
}

public abstract class ApiBase {

    Uri _endpoint;
    Microsoft.OneFuzz.Service.Request _request;
    internal ITestOutputHelper _output;

    public ApiBase(Uri endpoint, string relativeUri, Microsoft.OneFuzz.Service.Request request, ITestOutputHelper output) {
        _request = request;
        _endpoint = new Uri(endpoint, relativeUri);
        _output = output;
    }
    public async Task<Result<Stream, (HttpStatusCode, Error?)>> QueryGet(string? query) {
        Uri uri;
        if (String.IsNullOrEmpty(query))
            uri = _endpoint;
        else
            uri = new Uri($"{_endpoint}?{query}");
        var r = await _request.Get(uri);
        if (r.IsSuccessStatusCode) {
            return Result<Stream, (HttpStatusCode, Error?)>.Ok(r.Content.ReadAsStream());
        } else if (r.StatusCode == HttpStatusCode.InternalServerError) {
            return Result<Stream, (HttpStatusCode, Error?)>.Error((r.StatusCode, null));
        } else {
            var e = (await JsonDocument.ParseAsync(r.Content.ReadAsStream())).RootElement;
            return Result<Stream, (HttpStatusCode, Error?)>.Error((r.StatusCode, new Error(e)));
        }
    }

    public async Task<JsonElement> Get(JsonObject root, string? subPath = null) {
        var body = root.ToJsonString();
        var r = await _request.Get(subPath is null ? _endpoint : new Uri($"{_endpoint}{subPath}"), body);
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

    public static Result<IEnumerable<T>, Error> IEnumerableResult<T>(JsonElement res) where T : IFromJsonElement<T> {
        if (Error.IsError(res)) {
            return Result<IEnumerable<T>, Error>.Error(new Error(res));
        } else {
            if (res.ValueKind == JsonValueKind.Array)
                return Result<IEnumerable<T>, Error>.Ok(res.EnumerateArray().Select(T.Convert));
            else {
                var r = Result<T>(res);
                if (r.IsOk)
                    return Result<IEnumerable<T>, Error>.Ok(new[] { r.OkV });
                else
                    return Result<IEnumerable<T>, Error>.Error(r.ErrorV);
            }
        }
    }
    public static Result<T, Error> Result<T>(JsonElement res) where T : IFromJsonElement<T> {
        if (Error.IsError(res)) {
            return Result<T, Error>.Error(new Error(res));
        } else {
            Assert.True(res.ValueKind != JsonValueKind.Array);
            return Result<T, Error>.Ok(T.Convert(res));
        }
    }

    public static T Return<T>(JsonElement res) where T : IFromJsonElement<T> {
        return T.Convert(res);
    }

}
