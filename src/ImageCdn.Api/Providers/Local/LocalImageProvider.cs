using System.Text.Json;
using ImageCdn.Api.Models;
using ImageCdn.Api.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace ImageCdn.Api.Providers.Local;

public sealed class LocalImageProvider : IImageProvider
{
    private readonly LocalImageOptions _options;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly string _rootFullPath;

    public LocalImageProvider(
        IOptions<LocalImageOptions> options,
        IHttpContextAccessor httpContextAccessor)
    {
        _options = options.Value;
        _httpContextAccessor = httpContextAccessor;
        _rootFullPath = Path.GetFullPath(_options.Root);
        Directory.CreateDirectory(_rootFullPath);
    }

    public async Task<ImageAsset> UploadAsync(
        Stream stream,
        string fileName,
        string contentType,
        string path,
        CancellationToken cancellationToken)
    {
        var target = ResolveSafePath(path);
        if (File.Exists(target) || File.Exists(GetMetaPath(target)))
        {
            throw new ImageConflictException(path);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        await using (var fs = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await stream.CopyToAsync(fs, cancellationToken);
        }

        var uploaded = DateTimeOffset.UtcNow;
        var meta = new LocalImageMeta(fileName, contentType, uploaded);
        await File.WriteAllTextAsync(GetMetaPath(target), JsonSerializer.Serialize(meta), cancellationToken);

        return new ImageAsset
        {
            Id = path,
            Url = BuildPublicUrl(path),
            Filename = fileName,
            ContentType = contentType,
            Uploaded = uploaded
        };
    }

    public async Task<ImageAsset?> GetAsync(string path, CancellationToken cancellationToken)
    {
        var target = ResolveSafePath(path);
        if (!File.Exists(target))
        {
            return null;
        }

        LocalImageMeta? meta = null;
        var metaPath = GetMetaPath(target);
        if (File.Exists(metaPath))
        {
            var json = await File.ReadAllTextAsync(metaPath, cancellationToken);
            meta = JsonSerializer.Deserialize<LocalImageMeta>(json);
        }

        return new ImageAsset
        {
            Id = path,
            Url = BuildPublicUrl(path),
            Filename = meta?.Filename,
            ContentType = meta?.ContentType,
            Uploaded = meta?.Uploaded ?? File.GetCreationTimeUtc(target)
        };
    }

    public async Task<ImageAsset> ReplaceAsync(
        Stream stream,
        string fileName,
        string contentType,
        string path,
        CancellationToken cancellationToken)
    {
        var existing = await GetAsync(path, cancellationToken);
        if (existing is null)
        {
            throw new ImageNotFoundException(path);
        }

        var deleted = await DeleteAsync(path, cancellationToken);
        if (!deleted)
        {
            throw new ImageNotFoundException(path);
        }

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        return await UploadAsync(stream, fileName, contentType, path, cancellationToken);
    }

    public Task<bool> DeleteAsync(string path, CancellationToken cancellationToken)
    {
        var target = ResolveSafePath(path);
        var metaPath = GetMetaPath(target);
        var existed = File.Exists(target) || File.Exists(metaPath);

        if (File.Exists(target))
        {
            File.Delete(target);
        }

        if (File.Exists(metaPath))
        {
            File.Delete(metaPath);
        }

        return Task.FromResult(existed);
    }

    /// <summary>
    /// Maps a logical path under the configured root and rejects traversal escapes.
    /// </summary>
    public string ResolveSafePath(string path)
    {
        var combined = Path.GetFullPath(Path.Combine(_rootFullPath, path.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSep = _rootFullPath.TrimEnd(Path.DirectorySeparatorChar)
                          + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(combined, _rootFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resolved path escapes the configured local image root.");
        }

        return combined;
    }

    private string BuildPublicUrl(string path)
    {
        var http = _httpContextAccessor.HttpContext?.Request;
        var prefix = _options.PublicPathPrefix.TrimEnd('/');
        if (http is null)
        {
            return $"{prefix}/{path}";
        }

        return $"{http.Scheme}://{http.Host}{prefix}/{path}";
    }

    private static string GetMetaPath(string imagePath) => imagePath + ".meta.json";

    private sealed record LocalImageMeta(string Filename, string ContentType, DateTimeOffset Uploaded);
}
