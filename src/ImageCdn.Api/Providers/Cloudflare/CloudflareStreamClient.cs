using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ImageCdn.Api.Options;
using Microsoft.Extensions.Options;

namespace ImageCdn.Api.Providers.Cloudflare;

public sealed class CloudflareStreamClient
{
    public const string HttpClientName = "CloudflareStream";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CloudflareOptions _options;
    private readonly ILogger<CloudflareStreamClient> _logger;

    public CloudflareStreamClient(
        IHttpClientFactory httpClientFactory,
        IOptions<CloudflareOptions> options,
        ILogger<CloudflareStreamClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CloudflareDirectUploadResult> CreateDirectUploadAsync(
        CloudflareDirectUploadRequest payload,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"accounts/{_options.AccountId}/stream/direct_upload")
        {
            Content = JsonContent.Create(payload)
        };

        return await SendAsync<CloudflareDirectUploadResult>(request, cancellationToken);
    }

    public async Task<CloudflareStreamVideoResult?> GetAsync(
        string uid,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"accounts/{_options.AccountId}/stream/{Uri.EscapeDataString(uid)}");

        try
        {
            return await SendAsync<CloudflareStreamVideoResult>(request, cancellationToken);
        }
        catch (VideoNotFoundException)
        {
            return null;
        }
    }

    public async Task<bool> DeleteAsync(
        string uid,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"accounts/{_options.AccountId}/stream/{Uri.EscapeDataString(uid)}");

        try
        {
            await SendAsync<object>(request, cancellationToken, allowEmptyResult: true);
            return true;
        }
        catch (VideoNotFoundException)
        {
            return false;
        }
    }

    private void EnsureConfigured()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(_options.AccountId))
            missing.Add("CLOUDFLARE_ACCOUNT_ID");
        if (string.IsNullOrWhiteSpace(_options.ApiToken))
            missing.Add("CLOUDFLARE_API_TOKEN");

        if (missing.Count > 0)
        {
            throw new ProviderUnavailableException(
                $"Cloudflare Stream requires: {string.Join(", ", missing)}.");
        }
    }

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
            _logger.LogError(ex, "Cloudflare Stream request failed to reach upstream.");
            throw new UpstreamProviderException(
                "Unable to reach Cloudflare Stream.",
                statusCode: 502);
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
                        "Failed to parse Cloudflare Stream response. HTTP {StatusCode}",
                        (int)response.StatusCode);
                    throw new UpstreamProviderException(
                        "Cloudflare Stream returned an unreadable response.",
                        statusCode: 502);
                }
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new VideoNotFoundException("requested");
            }

            if (!response.IsSuccessStatusCode || envelope is { Success: false })
            {
                var firstError = envelope?.Errors.FirstOrDefault();
                _logger.LogWarning(
                    "Cloudflare Stream request failed. HTTP {StatusCode}, Code {ProviderCode}, Message {ProviderMessage}",
                    (int)response.StatusCode,
                    firstError?.Code,
                    firstError?.Message);

                throw new UpstreamProviderException(
                    firstError?.Message ?? "Cloudflare Stream request failed.",
                    statusCode: 502,
                    providerCode: firstError?.Code.ToString());
            }

            if (allowEmptyResult)
            {
                return envelope?.Result ?? (object)new object() as T
                    ?? throw new UpstreamProviderException(
                        "Cloudflare Stream returned an unexpected response.",
                        statusCode: 502);
            }

            if (envelope?.Result is null)
            {
                throw new UpstreamProviderException(
                    "Cloudflare Stream returned an empty result.",
                    statusCode: 502);
            }

            return envelope.Result;
        }
    }
}
