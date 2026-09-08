namespace ImageCdn.Api.Models;

public sealed class ImageAsset
{
    public required string Id { get; init; }
    public required string Url { get; init; }
    public string? Filename { get; init; }
    public string? ContentType { get; init; }
    public DateTimeOffset? Uploaded { get; init; }
}
