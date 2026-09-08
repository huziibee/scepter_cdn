using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ImageCdn.Api.Options;
using ImageCdn.Api.Providers.Cloudflare;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ImageCdn.Api.Tests;

public sealed class CloudflareImagesClientTests
{
    private const string Token = "test-token-value";

    [Fact]
    public async Task Upload_sends_correct_url_bearer_and_multipart_fields()
    {
        HttpRequestMessage? captured = null;
        string? bodyText = null;

        var handler = new StubHandler(async (request, _) =>
        {
            captured = request;
            bodyText = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync();

            var payload = """
                {
                  "success": true,
                  "errors": [],
                  "messages": [],
                  "result": {
                    "id": "products/nike/air-max/front",
                    "filename": "shoe.jpg",
                    "uploaded": "2026-09-08T18:00:00Z",
                    "variants": [
                      "https://imagedelivery.net/ACCOUNT_HASH/products/nike/air-max/front/public"
                    ]
                  }
                }
                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        });

        var client = CreateClient(handler);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("fake-image"));
        var result = await client.UploadAsync(stream, "shoe.jpg", "image/jpeg", "products/nike/air-max/front", CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("https://api.cloudflare.com/client/v4/accounts/acc-123/images/v1", captured.RequestUri!.ToString());
        Assert.Equal("Bearer", captured.Headers.Authorization!.Scheme);
        Assert.Equal(Token, captured.Headers.Authorization.Parameter);

        Assert.False(string.IsNullOrWhiteSpace(bodyText));
        Assert.Contains("name=file", bodyText!.Replace("\"", string.Empty), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name=id", bodyText.Replace("\"", string.Empty), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("products/nike/air-max/front", bodyText, StringComparison.Ordinal);
        Assert.Equal("products/nike/air-max/front", result.Id);
        Assert.Equal("shoe.jpg", result.Filename);
    }

    [Fact]
    public async Task Get_encodes_nested_custom_id()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler((request, _) =>
        {
            captured = request;
            var payload = """
                {
                  "success": true,
                  "errors": [],
                  "messages": [],
                  "result": { "id": "products/nike/air-max/front", "filename": "shoe.jpg" }
                }
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        });

        var client = CreateClient(handler);
        await client.GetAsync("products/nike/air-max/front", CancellationToken.None);

        Assert.NotNull(captured);
        var expectedEncoded = Uri.EscapeDataString("products/nike/air-max/front");
        Assert.Equal(
            $"https://api.cloudflare.com/client/v4/accounts/acc-123/images/v1/{expectedEncoded}",
            captured!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Delete_encodes_nested_custom_id()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler((request, _) =>
        {
            captured = request;
            var payload = """{ "success": true, "errors": [], "messages": [], "result": {} }""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        });

        var client = CreateClient(handler);
        await client.DeleteAsync("products/nike/air-max/front", CancellationToken.None);

        var expectedEncoded = Uri.EscapeDataString("products/nike/air-max/front");
        Assert.Equal(
            $"https://api.cloudflare.com/client/v4/accounts/acc-123/images/v1/{expectedEncoded}",
            captured!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Handles_cloudflare_4xx()
    {
        var handler = new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"success":false,"errors":[{"code":1000,"message":"bad"}],"messages":[],"result":null}""", Encoding.UTF8, "application/json")
            }));

        var client = CreateClient(handler);
        await using var stream = new MemoryStream([1, 2, 3]);
        var ex = await Assert.ThrowsAsync<UpstreamProviderException>(() =>
            client.UploadAsync(stream, "a.jpg", "image/jpeg", "x", CancellationToken.None));
        Assert.Equal(502, ex.StatusCode);
    }

    [Fact]
    public async Task Handles_cloudflare_5xx()
    {
        var handler = new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("boom", Encoding.UTF8, "text/plain")
            }));

        var client = CreateClient(handler);
        await using var stream = new MemoryStream([1, 2, 3]);
        var ex = await Assert.ThrowsAsync<UpstreamProviderException>(() =>
            client.UploadAsync(stream, "a.jpg", "image/jpeg", "x", CancellationToken.None));
        Assert.Equal(502, ex.StatusCode);
    }

    [Fact]
    public async Task Handles_success_false_envelope()
    {
        var handler = new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"success":false,"errors":[{"code":5409,"message":"Image already exists"}],"messages":[],"result":null}""", Encoding.UTF8, "application/json")
            }));

        var client = CreateClient(handler);
        await using var stream = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAsync<ImageConflictException>(() =>
            client.UploadAsync(stream, "a.jpg", "image/jpeg", "dup", CancellationToken.None));
    }

    [Fact]
    public void EncodeImageId_encodes_slashes()
    {
        Assert.Equal("products%2Fnike%2Fair-max%2Ffront", CloudflareImagesClient.EncodeImageId("products/nike/air-max/front"));
    }

    private static CloudflareImagesClient CreateClient(HttpMessageHandler handler)
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
        return new CloudflareImagesClient(factory, options, NullLogger<CloudflareImagesClient>.Instance);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
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
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            return client;
        }
    }
}
