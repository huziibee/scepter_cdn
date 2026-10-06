using System.Text.Json.Serialization;

namespace ImageCdn.Api.Providers.Cloudflare;

public sealed class CloudflareDirectUploadRequest
{
    [JsonPropertyName("maxDurationSeconds")]
    public int MaxDurationSeconds { get; set; }

    [JsonPropertyName("creator")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Creator { get; set; }

    [JsonPropertyName("allowedOrigins")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? AllowedOrigins { get; set; }

    [JsonPropertyName("requireSignedURLs")]
    public bool RequireSignedUrls { get; set; }

    [JsonPropertyName("meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Meta { get; set; }
}

public sealed class CloudflareDirectUploadResult
{
    [JsonPropertyName("uid")]
    public string? Uid { get; set; }

    [JsonPropertyName("uploadURL")]
    public string? UploadUrl { get; set; }
}

public sealed class CloudflareStreamVideoResult
{
    [JsonPropertyName("uid")]
    public string? Uid { get; set; }

    [JsonPropertyName("preview")]
    public string? Preview { get; set; }

    [JsonPropertyName("thumbnail")]
    public string? Thumbnail { get; set; }

    [JsonPropertyName("readyToStream")]
    public bool ReadyToStream { get; set; }

    [JsonPropertyName("requireSignedURLs")]
    public bool RequireSignedUrls { get; set; }

    [JsonPropertyName("duration")]
    public double? Duration { get; set; }

    [JsonPropertyName("size")]
    public long? Size { get; set; }

    [JsonPropertyName("uploaded")]
    public DateTimeOffset? Uploaded { get; set; }

    [JsonPropertyName("creator")]
    public string? Creator { get; set; }

    [JsonPropertyName("playback")]
    public CloudflareStreamPlayback? Playback { get; set; }

    [JsonPropertyName("status")]
    public CloudflareStreamStatus? Status { get; set; }
}

public sealed class CloudflareStreamPlayback
{
    [JsonPropertyName("hls")]
    public string? Hls { get; set; }

    [JsonPropertyName("dash")]
    public string? Dash { get; set; }
}

public sealed class CloudflareStreamStatus
{
    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("pctComplete")]
    public string? PctComplete { get; set; }

    [JsonPropertyName("errorReasonCode")]
    public string? ErrorReasonCode { get; set; }

    [JsonPropertyName("errorReasonText")]
    public string? ErrorReasonText { get; set; }
}
