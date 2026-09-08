using System.Text.Json.Serialization;

namespace ImageCdn.Api.Providers.Cloudflare;

public sealed class CloudflareApiResponse<T>
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("errors")]
    public List<CloudflareApiError> Errors { get; set; } = [];

    [JsonPropertyName("messages")]
    public List<CloudflareApiMessage> Messages { get; set; } = [];

    [JsonPropertyName("result")]
    public T? Result { get; set; }
}

public sealed class CloudflareApiError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public sealed class CloudflareApiMessage
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public sealed class CloudflareImageResult
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("filename")]
    public string? Filename { get; set; }

    [JsonPropertyName("uploaded")]
    public DateTimeOffset? Uploaded { get; set; }

    [JsonPropertyName("requireSignedURLs")]
    public bool RequireSignedUrls { get; set; }

    [JsonPropertyName("variants")]
    public List<string>? Variants { get; set; }

    [JsonPropertyName("meta")]
    public Dictionary<string, string>? Meta { get; set; }
}
