﻿using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Microsoft.OneFuzz.Service;
using AccessToken = String;
using TokenType = String;

public class Request {
    private readonly HttpClient _httpClient;

    private readonly Func<Task<(TokenType, AccessToken)>>? _auth;

    public Request(HttpClient httpClient, Func<Task<(TokenType, AccessToken)>>? auth = null) {
        _auth = auth;
        _httpClient = httpClient;
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, Uri url, HttpContent? content = null, IDictionary<string, string>? headers = null) {
        using var request = new HttpRequestMessage(method: method, requestUri: url);

        if (_auth is not null) {
            var (tokenType, accessToken) = await _auth();
            request.Headers.Authorization = new AuthenticationHeaderValue(tokenType, accessToken);
        }

        if (content is not null) {
            request.Content = content;
        }

        if (headers is not null) {
            foreach (var v in headers) {
                request.Headers.Add(v.Key, v.Value);
            }
        }

        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }

    public async Task<HttpResponseMessage> Get(Uri url, string? json = null) {
        if (json is not null) {
            using var b = new StringContent(json);
            b.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
            return await Send(method: HttpMethod.Get, url: url, content: b);
        } else {
            return await Send(method: HttpMethod.Get, url: url);
        }
    }
    public async Task<HttpResponseMessage> Delete(Uri url, string? json = null) {
        if (json is not null) {
            using var b = new StringContent(json);
            b.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
            return await Send(method: HttpMethod.Delete, url: url, content: b);
        } else {
            return await Send(method: HttpMethod.Delete, url: url);
        }
    }

    public async Task<HttpResponseMessage> Post(Uri url, String json, IDictionary<string, string>? headers = null) {
        using var b = new StringContent(json);
        b.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
        return await Send(method: HttpMethod.Post, content: b, url: url, headers: headers);
    }

    public async Task<HttpResponseMessage> Put(Uri url, String json, IDictionary<string, string>? headers = null) {
        using var b = new StringContent(json);
        b.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
        return await Send(method: HttpMethod.Put, content: b, url: url, headers: headers);
    }

    public async Task<HttpResponseMessage> Patch(Uri url, String json, IDictionary<string, string>? headers = null) {
        using var b = new StringContent(json);
        b.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
        return await Send(method: HttpMethod.Patch, content: b, url: url, headers: headers);
    }
}
