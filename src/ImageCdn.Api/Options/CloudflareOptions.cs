namespace ImageCdn.Api.Options;

public sealed class CloudflareOptions
{
    public const string SectionName = "Cloudflare";

    public string AccountId { get; set; } = string.Empty;
    public string AccountHash { get; set; } = string.Empty;
    public string ApiToken { get; set; } = string.Empty;
    public string Variant { get; set; } = "public";
    public string ApiBaseUrl { get; set; } = "https://api.cloudflare.com/client/v4/";

    public void EnsureValid()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(AccountId))
            missing.Add("CLOUDFLARE_ACCOUNT_ID");
        if (string.IsNullOrWhiteSpace(AccountHash))
            missing.Add("CLOUDFLARE_ACCOUNT_HASH");
        if (string.IsNullOrWhiteSpace(ApiToken))
            missing.Add("CLOUDFLARE_API_TOKEN");

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"IMAGE_PROVIDER=Cloudflare requires the following configuration values: {string.Join(", ", missing)}.");
        }
    }
}
