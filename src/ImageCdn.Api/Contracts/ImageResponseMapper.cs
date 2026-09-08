using ImageCdn.Api.Models;

namespace ImageCdn.Api.Contracts;

public static class ImageResponseMapper
{
    public static ImageResponse FromAsset(ImageAsset asset) =>
        new(asset.Id, asset.Url, asset.Filename, asset.ContentType, asset.Uploaded);
}
