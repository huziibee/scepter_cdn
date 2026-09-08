namespace ImageCdn.Api.Providers;

public interface IImageProvider
{
    Task<Models.ImageAsset> UploadAsync(
        Stream stream,
        string fileName,
        string contentType,
        string path,
        CancellationToken cancellationToken);

    Task<Models.ImageAsset?> GetAsync(
        string path,
        CancellationToken cancellationToken);

    Task<Models.ImageAsset> ReplaceAsync(
        Stream stream,
        string fileName,
        string contentType,
        string path,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        string path,
        CancellationToken cancellationToken);
}
