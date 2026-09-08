namespace ImageCdn.Api.Options;

public sealed class ImageValidationOptions
{
    public const string SectionName = "ImageValidation";

    public long MaxImageSizeBytes { get; set; } = 10 * 1024 * 1024;

    public int MaxPathLength { get; set; } = 1024;

    public string[] AllowedContentTypes { get; set; } =
    [
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif",
        "image/svg+xml"
    ];
}
