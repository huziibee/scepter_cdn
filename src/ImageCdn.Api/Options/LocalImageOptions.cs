namespace ImageCdn.Api.Options;

public sealed class LocalImageOptions
{
    public const string SectionName = "LocalImages";

    /// <summary>
    /// Root directory for locally stored images. Bound from LOCAL_IMAGE_ROOT.
    /// </summary>
    public string Root { get; set; } = ".local-data/images";

    /// <summary>
    /// Public URL prefix used when constructing local development image URLs.
    /// </summary>
    public string PublicPathPrefix { get; set; } = "/local-images";
}
