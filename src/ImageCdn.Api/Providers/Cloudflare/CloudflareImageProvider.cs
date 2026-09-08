using ImageCdn.Api.Models;
using ImageCdn.Api.Options;
using Microsoft.Extensions.Options;

namespace ImageCdn.Api.Providers.Cloudflare;

public sealed class CloudflareImageProvider : IImageProvider
{
    private readonly CloudflareImagesClient _client;
    private readonly ILogger<CloudflareImageProvider> _logger;

    public CloudflareImageProvider(
        CloudflareImagesClient client,
        IOptions<CloudflareOptions> options,
        ILogger<CloudflareImageProvider> logger)
    {
        _client = client;
        _logger = logger;
        options.Value.EnsureValid();
    }

    public async Task<ImageAsset> UploadAsync(
        Stream stream,
        string fileName,
        string contentType,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _client.UploadAsync(stream, fileName, contentType, path, cancellationToken);
            return Map(result, path, contentType);
        }
        catch (ImageConflictException)
        {
            throw new ImageConflictException(path);
        }
        catch (ImageNotFoundException)
        {
            throw;
        }
    }

    public async Task<ImageAsset?> GetAsync(string path, CancellationToken cancellationToken)
    {
        var result = await _client.GetAsync(path, cancellationToken);
        return result is null ? null : Map(result, path, contentType: null);
    }

    public async Task<ImageAsset> ReplaceAsync(
        Stream stream,
        string fileName,
        string contentType,
        string path,
        CancellationToken cancellationToken)
    {
        // Non-atomic replacement: Cloudflare has no binary overwrite endpoint.
        // Verify → delete → upload using the same custom ID.
        var existing = await GetAsync(path, cancellationToken);
        if (existing is null)
        {
            throw new ImageNotFoundException(path);
        }

        try
        {
            await _client.DeleteAsync(path, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to delete image {Path} during replace.", path);
            throw new UpstreamProviderException(
                $"Failed to delete existing image '{path}' during replacement.",
                statusCode: 502);
        }

        try
        {
            // Reset stream if possible for upload after delete.
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            var uploaded = await _client.UploadAsync(stream, fileName, contentType, path, cancellationToken);
            return Map(uploaded, path, contentType);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not UpstreamProviderException and not ImageConflictException)
        {
            _logger.LogError(
                ex,
                "Failed to re-upload image {Path} after delete during replace. The previous image may already be deleted.",
                path);
            throw new UpstreamProviderException(
                $"Failed to upload replacement for '{path}'. The previous image may already have been deleted (non-atomic replace).",
                statusCode: 502);
        }
    }

    public async Task<bool> DeleteAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await _client.DeleteAsync(path, cancellationToken);
            return true;
        }
        catch (ImageNotFoundException)
        {
            return false;
        }
    }

    private ImageAsset Map(CloudflareImageResult result, string path, string? contentType)
    {
        var id = string.IsNullOrWhiteSpace(result.Id) ? path : result.Id;
        return new ImageAsset
        {
            Id = id,
            Url = _client.BuildDeliveryUrl(id, result.Variants),
            Filename = result.Filename,
            ContentType = contentType,
            Uploaded = result.Uploaded
        };
    }
}
