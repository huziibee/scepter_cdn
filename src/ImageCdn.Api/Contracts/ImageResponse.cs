namespace ImageCdn.Api.Contracts;

public sealed record ImageResponse(
    string Id,
    string Url,
    string? Filename,
    string? ContentType,
    DateTimeOffset? Uploaded);

public sealed record HealthResponse(string Status, string Provider);
