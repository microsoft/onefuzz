﻿using System.ComponentModel.DataAnnotations;
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

// See: https://www.rfc-editor.org/rfc/rfc7807#section-3
public sealed class ProblemDetails {
    public ProblemDetails(HttpStatusCode code, Error error) {
        Status = (int)code;
        Title = error.Code.ToString();
        Detail = error.Errors?.Join("\n");
    }

    // We do not yet use the type/instance properties:

    /// A URI reference [RFC3986] that identifies the problem type.  This
    /// specification encourages that, when dereferenced, it provide
    /// human-readable documentation for the problem type (e.g., using HTML
    /// [W3C.REC-html5-20141028]).  When this member is not present, its value
    /// is assumed to be "about:blank".
    // public string? Type { get; set; } = "about:blank";

    /// A URI reference that identifies the specific occurrence of the problem.
    /// It may or may not yield further information if dereferenced.
    // public string? Instance { get; set; }

    /// A short, human-readable summary of the problem type.  It SHOULD NOT
    /// change from occurrence to occurrence of the problem, except for purposes
    /// of localization (e.g., using proactive content negotiation; see
    /// [RFC7231], Section 3.4).
    public string Title { get; set; }

    /// The HTTP status code ([RFC7231], Section 6) generated by the origin
    /// server for this occurrence of the problem.
    public int Status { get; set; }

    //  A human-readable explanation specific to this occurrence of the problem.
    public string? Detail { get; set; }
}

public class RequestHandling : IRequestHandling {
    private readonly ILogTracer _log;
    public RequestHandling(ILogTracer log) {
        _log = log;
    }
    public async Async.Task<HttpResponseData> NotOk(HttpRequestData request, Error error, string context, HttpStatusCode statusCode = HttpStatusCode.BadRequest) {
        var statusNum = (int)statusCode;
        if (statusNum >= 400 && statusNum <= 599) {
            _log.Error($"request error: {context:Tag:Context} - {error:Tag:Error}");

            // emit standardized errors according to RFC7807:
            // https://www.rfc-editor.org/rfc/rfc7807
            var response = request.CreateResponse();
            await response.WriteAsJsonAsync(
                new ProblemDetails(statusCode, error),
                "application/problem+json",
                statusCode);

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
