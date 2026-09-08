namespace ImageCdn.Api.Options;

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>
    /// Optional inbound API key. Bound from SERVICE_API_KEY.
    /// When empty in Development, /api/* may be unauthenticated.
    /// </summary>
    public string? ServiceApiKey { get; set; }
}