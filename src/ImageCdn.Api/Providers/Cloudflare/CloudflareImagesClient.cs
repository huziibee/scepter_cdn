using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ImageCdn.Api.Options;
using Microsoft.Extensions.Options;

namespace ImageCdn.Api.Providers.Cloudflare;

public sealed class CloudflareImagesClient
{
    public const string HttpClientName = "CloudflareImages";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CloudflareOptions _options;
    private readonly ILogger<CloudflareImagesClient> _logger;

    public CloudflareImagesClient(
        IHttpClientFactory httpClientFactory,
        IOptions<CloudflareOptions> options,
        ILogger<CloudflareImagesClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CloudflareImageResult> UploadAsync(
        Stream stream,
        string fileName,
        string contentType,
        string imageId,
        CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent();

        var streamContent = new StreamContent(stream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(streamContent, "file", fileName);
        content.Add(new StringContent(imageId), "id");

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"accounts/{_options.AccountId}/images/v1")
        {
            Content = content
        };

        return await SendAsync<CloudflareImageResult>(request, cancellationToken);
    }

    public async Task<CloudflareImageResult?> GetAsync(string imageId, CancellationToken cancellationToken)
    {
        var encodedId = EncodeImageId(imageId);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"accounts/{_options.AccountId}/images/v1/{encodedId}");

        try
        {
            return await SendAsync<CloudflareImageResult>(request, cancellationToken);
        }
        catch (ImageNotFoundException)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string imageId, CancellationToken cancellationToken)
    {
        var encodedId = EncodeImageId(imageId);
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"accounts/{_options.AccountId}/images/v1/{encodedId}");

        await SendAsync<CloudflareImageResult>(request, cancellationToken, allowEmptyResult: true);
    }

    public string BuildDeliveryUrl(string imageId, IReadOnlyList<string>? variants = null)
    {
        if (variants is { Count: > 0 })
        {
            var preferred = variants.FirstOrDefault(v =>
                v.Contains($"/{_options.Variant}", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(preferred))
            {
                return preferred;
            }

            if (!string.IsNullOrWhiteSpace(variants[0]))
            {
                return variants[0];
            }
        }

        var encodedSegments = string.Join(
            '/',
            imageId.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));

        return $"https://imagedelivery.net/{_options.AccountHash}/{encodedSegments}/{_options.Variant}";
    }

    /// <summary>
    /// Cloudflare expects the full custom image ID URL-encoded as a single path segment
    /// (including encoded slashes for nested IDs).
    /// </summary>
    public static string EncodeImageId(string imageId) =>
        Uri.EscapeDataString(imageId);

    private async Task<T> SendAsync<T>(
        HttpRequestMessage request,
        CancellationToken cancellationToken,
        bool allowEmptyResult = false) where T : class
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Cloudflare Images request failed to reach upstream.");
            throw new UpstreamProviderException("Unable to reach Cloudflare Images.", statusCode: 502);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            CloudflareApiResponse<T>? envelope = null;
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    envelope = JsonSerializer.Deserialize<CloudflareApiResponse<T>>(body, JsonOptions);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to parse Cloudflare response. HTTP {StatusCode}",
                        (int)response.StatusCode);
                    throw new UpstreamProviderException(
                        "Cloudflare returned an unreadable response.",
                        statusCode: 502);
                }
            }

            if (response.StatusCode == HttpStatusCode.NotFound ||
                envelope?.Errors.Any(e => e.Code == 5404 || e.Message?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true) == true)
            {
                throw new ImageNotFoundException("requested");
            }

            if ((int)response.StatusCode == 409 ||
                envelope?.Errors.Any(e => e.Code == 5409 || e.Message?.Contains("already exists", StringComparison.OrdinalIgnoreCase) == true) == true)
            {
                throw new ImageConflictException("requested");
            }

            if (!response.IsSuccessStatusCode || envelope is { Success: false })
            {
                var firstError = envelope?.Errors.FirstOrDefault();
                _logger.LogWarning(
                    "Cloudflare Images request failed. HTTP {StatusCode}, Code {ProviderCode}, Message {ProviderMessage}",
                    (int)response.StatusCode,
                    firstError?.Code,
                    firstError?.Message);

                if ((int)response.StatusCode >= 500 || (!response.IsSuccessStatusCode && envelope is null))
                {
                    throw new UpstreamProviderException(
                        "Cloudflare Images upstream failure.",
                        statusCode: 502,
                        providerCode: firstError?.Code.ToString());
                }

                throw new UpstreamProviderException(
                    firstError?.Message ?? "Cloudflare Images request failed.",
                    statusCode: 502,
                    providerCode: firstError?.Code.ToString());
            }

            if (allowEmptyResult)
            {
                return (envelope?.Result)!;
            }

            if (envelope?.Result is null)
            {
                throw new UpstreamProviderException(
                    "Cloudflare Images returned an empty result.",
                    statusCode: 502);
            }

            return envelope.Result;
        }
    }
}