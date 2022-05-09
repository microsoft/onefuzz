﻿using System.Net;
using System.Text.Json;
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

    public static async Async.Task<OneFuzzResult<T>> ParseRequest<T>(HttpRequestData req) {
        Exception? exception = null;
        try {
            var t = await req.ReadFromJsonAsync<T>();
            if (t != null) {
                return OneFuzzResult<T>.Ok(t);
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

    public async static Async.Task<HttpResponseData> Ok(HttpResponseData resp, IEnumerable<BaseResponse> response) {
        // var resp = req.CreateResponse();
        resp.StatusCode = HttpStatusCode.OK;
        if (response.Count() > 1) {
            await resp.WriteAsJsonAsync(response);
            return resp;
        } else if (response.Any()) {
            var t = JsonSerializer.Serialize(response.Single(), EntityConverter.GetJsonSerializerOptions());
            await resp.WriteStringAsync(t);
        }
        // TODO: ModelMixin stuff

        return resp;
    }

    public async static Async.Task<HttpResponseData> Ok(HttpResponseData resp, BaseResponse response) {
        return await Ok(resp, new BaseResponse[] { response });
    }
}

