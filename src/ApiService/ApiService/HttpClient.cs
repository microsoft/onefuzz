using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net.Http.Headers;

namespace Microsoft.OneFuzz.Service;
using TokenType = String;
using AccessToken = String;

public class Request {
    private static HttpClient httpClient = new HttpClient();

    Func<Task<Tuple<TokenType, AccessToken>>>? auth;

    public Request(Func<Task<Tuple<TokenType, AccessToken>>>? auth = null) {
        this.auth = auth;
    }
    
    private async Task<HttpResponseMessage> Send(HttpMethod method, Uri url, HttpContent? content = null, IDictionary<string, string>? headers = null) {
        var request = new HttpRequestMessage(method: method, requestUri: url);

        if (auth is not null) {
            Tuple<TokenType, AccessToken> token = await auth();
            request.Headers.Authorization = new AuthenticationHeaderValue(token.Item1, token.Item2);
        }

        if (content is not null) {
            request.Content = content;
        }

        if (headers is not null) {
            foreach(var v in headers) {
                request.Headers.Add(v.Key, v.Value);
            }
        }

        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }

    public async Task<HttpResponseMessage> Get(Uri url) {
        return await Send(method: HttpMethod.Get, url: url);
    }
    public async Task<HttpResponseMessage> Delete(Uri url)
    {
        return await Send(method: HttpMethod.Delete, url: url);
    }

    public async Task<HttpResponseMessage> Post(Uri url, String json, IDictionary<string, string>? headers = null) {
        using var b = new StringContent(json);
        b.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
        return await Send(method: HttpMethod.Post, url: url, headers:headers);
    }

    public async Task<HttpResponseMessage> Put(Uri url, String json, IDictionary<string, string>? headers = null) {
        using var b = new StringContent(json);
        b.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
        return await Send(method: HttpMethod.Put, url: url, headers: headers);
    }

    public async Task<HttpResponseMessage> Patch(Uri url, String json, IDictionary<string, string>? headers = null)
    {
        using var b = new StringContent(json);
        b.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
        return await Send(method: HttpMethod.Patch, url: url, headers: headers);
    }
}
