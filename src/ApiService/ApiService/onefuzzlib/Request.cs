using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Faithlife.Utility;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface IRequestHandling {
    Async.Task<HttpResponseData> NotOk(HttpRequestData request, Error error, string context, HttpStatusCode statusCode = HttpStatusCode.BadRequest);
}

public class RequestHandling : IRequestHandling {
    private readonly ILogTracer _log;
    public RequestHandling(ILogTracer log) {
        _log = log;
    }
    public async Async.Task<HttpResponseData> NotOk(HttpRequestData request, Error error, string context, HttpStatusCode statusCode = HttpStatusCode.BadRequest) {
        var statusNum = (int)statusCode;
        if (statusNum >= 400 && statusNum <= 599) {
            _log.Error($"request error - {context}: {error}");

            var response = HttpResponseData.CreateResponse(request);
            await response.WriteAsJsonAsync(error);
            response.StatusCode = statusCode;
            return response;
        }

        throw new ArgumentOutOfRangeException($"status code {statusCode} - {statusNum} is not in the expected range [400; 599]");
    }

    public static async Async.Task<OneFuzzResult<T>> ParseRequest<T>(HttpRequestData req)
        where T : BaseRequest {
        Exception? exception = null;
        try {
            var t = await req.ReadFromJsonAsync<T>();
            if (t != null) {

                // ExtensionData is used here to detect if there are any unknown 
                // properties set:
                if (t.ExtensionData != null) {
                    var errors = new List<string>();
                    foreach (var (name, value) in t.ExtensionData) {
                        // allow additional properties if they are null,
                        // otherwise produce an error
                        if (value.ValueKind != JsonValueKind.Null) {
                            errors.Add($"Unexpected property: \"{name}\"");
                        }
                    }

                    if (errors.Any()) {
                        return new Error(
                            Code: ErrorCode.INVALID_REQUEST,
                            Errors: errors.ToArray());
                    }
                }

                var validationContext = new ValidationContext(t);
                var validationResults = new List<ValidationResult>();
                if (Validator.TryValidateObject(t, validationContext, validationResults, validateAllProperties: true)) {
                    return OneFuzzResult.Ok(t);
                } else {
                    return new Error(
                        Code: ErrorCode.INVALID_REQUEST,
                        Errors: validationResults.Select(vr => vr.ToString()).ToArray());
                }
            } else {
                return OneFuzzResult<T>.Error(
                    ErrorCode.INVALID_REQUEST,
                    $"Failed to deserialize message into type: {typeof(T)} - null");
            }
        } catch (Exception e) {
            exception = e;
        }

        if (exception != null) {
            return OneFuzzResult<T>.Error(ConvertError(exception));
        }
        return OneFuzzResult<T>.Error(
            ErrorCode.INVALID_REQUEST,
            $"Failed to deserialize message into type: {typeof(T)} - {await req.ReadAsStringAsync()}"
        );
    }

    public static async Async.Task<OneFuzzResult<T>> ParseUri<T>(HttpRequestData req) {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var doc = new JsonObject();
        foreach (var key in query.AllKeys.WhereNotNull()) {
            doc[key] = JsonValue.Create(query[key]);
        }

        try {
            var result = doc.Deserialize<T>(EntityConverter.GetJsonSerializerOptions());
            return result switch {
                null => OneFuzzResult<T>.Error(
                    ErrorCode.INVALID_REQUEST,
                    $"Failed to deserialize message into type: {typeof(T)} - {await req.ReadAsStringAsync()}"
                ),
                var r => OneFuzzResult<T>.Ok(r),
            };

        } catch (JsonException exception) {
            return OneFuzzResult<T>.Error(ConvertError(exception));
        }
    }

    public static Error ConvertError(Exception exception) {
        return new Error(
            ErrorCode.INVALID_REQUEST,
            new string[] {
                exception.Message,
                exception.Source ?? string.Empty,
                exception.StackTrace ?? string.Empty
            }
        );
    }

    public static HttpResponseData Redirect(HttpRequestData req, Uri uri) {
        var resp = req.CreateResponse();
        resp.StatusCode = HttpStatusCode.Found;
        resp.Headers.Add("Location", uri.ToString());
        return resp;
    }

    public static async Async.ValueTask<HttpResponseData> Ok(HttpRequestData req, IEnumerable<BaseResponse> response) {
        // TODO: ModelMixin stuff
        var resp = req.CreateResponse();
        resp.StatusCode = HttpStatusCode.OK;
        await resp.WriteAsJsonAsync(response);
        return resp;
    }

    public static async Async.ValueTask<HttpResponseData> Ok(HttpRequestData req, BaseResponse response) {
        // TODO: ModelMixin stuff
        var resp = req.CreateResponse();
        resp.StatusCode = HttpStatusCode.OK;
        await resp.WriteAsJsonAsync(response);
        return resp;
    }
}
