namespace ImageCdn.Api.Options;

public sealed class ImageProviderOptions
{
    public const string SectionName = "ImageProvider";

    /// <summary>
    /// Active provider name: Local or Cloudflare.
    /// Bound from IMAGE_PROVIDER.
    /// </summary>
    public string Provider { get; set; } = "Local";

    public bool IsLocal =>
        string.Equals(Provider, "Local", StringComparison.OrdinalIgnoreCase);

    public bool IsCloudflare =>
        string.Equals(Provider, "Cloudflare", StringComparison.OrdinalIgnoreCase);
}
