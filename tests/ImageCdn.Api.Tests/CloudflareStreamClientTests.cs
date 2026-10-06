using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ImageCdn.Api.Options;
using ImageCdn.Api.Providers.Cloudflare;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImageCdn.Api.Tests;

public sealed class CloudflareStreamClientTests
{
    private const string Token = "test-token-value";

    [Fact]
    public async Task Direct_upload_uses_stream_endpoint_and_bearer_token()
    {
        HttpRequestMessage? captured = null;
        string? body = null;

        var handler = new StubHandler(async (request, _) =>
        {
            captured = request;
            body = request.Content is null ? null : await request.Content.ReadAsStringAsync();

            return JsonResponse("""
                {
                  "success": true,
                  "errors": [],
                  "messages": [],
                  "result": {
                    "uid": "f65014bc6ff5419ea86e7972a047ba22",
                    "uploadURL": "https://upload.videodelivery.net/f65014bc6ff5419ea86e7972a047ba22"
                  }
                }
                """);
        });

        var client = CreateClient(handler);
        var result = await client.CreateDirectUploadAsync(
            new CloudflareDirectUploadRequest
            {
                MaxDurationSeconds = 600,
                Creator = "user-123",
                Meta = new Dictionary<string, string> { ["name"] = "clip.mp4" }
            },
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal(
            "https://api.cloudflare.com/client/v4/accounts/acc-123/stream/direct_upload",
            captured.RequestUri!.ToString());
        Assert.Equal("Bearer", captured.Headers.Authorization!.Scheme);
        Assert.Equal(Token, captured.Headers.Authorization.Parameter);
        Assert.Contains(""maxDurationSeconds":600", body);
        Assert.Contains(""creator":"user-123"", body);
        Assert.Equal("f65014bc6ff5419ea86e7972a047ba22", result.Uid);
        Assert.StartsWith("https://upload.videodelivery.net/", result.UploadUrl);
    }

    [Fact]
    public async Task Get_returns_video_playback_and_status()
    {
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal(
                "https://api.cloudflare.com/client/v4/accounts/acc-123/stream/abc123",
                request.RequestUri!.ToString());

            return Task.FromResult(JsonResponse("""
                {
                  "success": true,
                  "errors": [],
                  "messages": [],
                  "result": {
                    "uid": "abc123",
                    "readyToStream": true,
                    "preview": "https://customer.example/abc123/watch",
                    "thumbnail": "https://customer.example/abc123/thumbnails/thumbnail.jpg",
                    "duration": 2.5,
                    "size": 12345,
                    "status": { "state": "ready", "pctComplete": "100" },
                    "playback": {
                      "hls": "https://customer.example/abc123/manifest/video.m3u8",
                      "dash": "https://customer.example/abc123/manifest/video.mpd"
                    }
                  }
                }
                """));
        });

        var client = CreateClient(handler);
        var result = await client.GetAsync("abc123", CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.ReadyToStream);
        Assert.Equal("ready", result.Status!.State);
        Assert.EndsWith(".m3u8", result.Playback!.Hls);
    }

    [Fact]
    public async Task Get_404_returns_null()
    {
        var handler = new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(
                    """{"success":false,"errors":[{"code":1000,"message":"not found"}],"messages":[],"result":null}""",
                    Encoding.UTF8,
                    "application/json")
            }));

        var client = CreateClient(handler);
        var result = await client.GetAsync("missing", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Delete_success_returns_true()
    {
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            return Task.FromResult(JsonResponse(
                """{"success":true,"errors":[],"messages":[],"result":null}"""));
        });

        var client = CreateClient(handler);
        Assert.True(await client.DeleteAsync("abc123", CancellationToken.None));
    }

    private static CloudflareStreamClient CreateClient(HttpMessageHandler handler)
    {
        var factory = new TestHttpClientFactory(handler, Token);
        var options = Microsoft.Extensions.Options.Options.Create(new CloudflareOptions
        {
            AccountId = "acc-123",
            AccountHash = "ACCOUNT_HASH",
            ApiToken = Token,
            Variant = "public",
            ApiBaseUrl = "https://api.cloudflare.com/client/v4/"
        });

        return new CloudflareStreamClient(
            factory,
            options,
            NullLogger<CloudflareStreamClient>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            _handler(request, cancellationToken);
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        private readonly string _token;

        public TestHttpClientFactory(HttpMessageHandler handler, string token)
        {
            _handler = handler;
            _token = token;
        }

        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient(_handler, disposeHandler: false)
            {
                BaseAddress = new Uri("https://api.cloudflare.com/client/v4/")
            };
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _token);
            return client;
        }
    }
}
