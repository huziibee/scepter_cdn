namespace ImageCdn.Api.Contracts;

public sealed class CreateVideoUploadRequest
{
    public int MaxDurationSeconds { get; set; } = 600;
    public string? Creator { get; set; }
    public string? FileName { get; set; }
    public List<string>? AllowedOrigins { get; set; }
    public bool RequireSignedUrls { get; set; }
}

public sealed record DirectVideoUploadResponse(
    string Uid,
    string UploadUrl);

public sealed record VideoPlaybackResponse(
    string? Hls,
    string? Dash);

public sealed record VideoStatusResponse(
    string? State,
    string? PctComplete,
    string? ErrorReasonCode,
    string? ErrorReasonText);

public sealed record VideoResponse(
    string Uid,
    bool ReadyToStream,
    string? Preview,
    string? Thumbnail,
    VideoPlaybackResponse? Playback,
    VideoStatusResponse? Status,
    double? Duration,
    long? Size,
    DateTimeOffset? Uploaded,
    string? Creator,
    bool RequireSignedUrls);
