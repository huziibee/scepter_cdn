namespace ImageCdn.Api;

public sealed class ImageNotFoundException : Exception
{
    public ImageNotFoundException(string path)
        : base($"Image '{path}' was not found.")
    {
        Path = path;
    }

    public string Path { get; }
}

public sealed class ImageConflictException : Exception
{
    public ImageConflictException(string path)
        : base($"An image already exists at path '{path}'.")
    {
        Path = path;
    }

    public string Path { get; }
}

public sealed class UpstreamProviderException : Exception
{
    public UpstreamProviderException(string message, int? statusCode = null, string? providerCode = null)
        : base(message)
    {
        StatusCode = statusCode;
        ProviderCode = providerCode;
    }

    public int? StatusCode { get; }
    public string? ProviderCode { get; }
}

public sealed class ProviderUnavailableException : Exception
{
    public ProviderUnavailableException(string message)
        : base(message)
    {
    }
}
