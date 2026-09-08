using ImageCdn.Api.Options;
using ImageCdn.Api.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace ImageCdn.Api.Tests;

public sealed class ImageFileValidatorTests
{
    private readonly ImageFileValidator _validator = new(Microsoft.Extensions.Options.Options.Create(new ImageValidationOptions
    {
        MaxImageSizeBytes = 1024
    }));

    [Fact]
    public void Rejects_missing_file()
    {
        var result = _validator.Validate(null);
        Assert.False(result.IsValid);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
    }

    [Fact]
    public void Rejects_zero_length_file()
    {
        var file = CreateFile(Array.Empty<byte>(), "image/jpeg", "empty.jpg");
        var result = _validator.Validate(file);
        Assert.False(result.IsValid);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
    }

    [Fact]
    public void Rejects_unsupported_mime()
    {
        var file = CreateFile([1, 2, 3], "application/pdf", "doc.pdf");
        var result = _validator.Validate(file);
        Assert.False(result.IsValid);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
    }

    [Fact]
    public void Rejects_oversized_file()
    {
        var file = CreateFile(new byte[2048], "image/png", "big.png");
        var result = _validator.Validate(file);
        Assert.False(result.IsValid);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, result.StatusCode);
    }

    [Fact]
    public void Accepts_valid_jpeg()
    {
        var file = CreateFile([1, 2, 3, 4], "image/jpeg", "shoe.jpg");
        var result = _validator.Validate(file);
        Assert.True(result.IsValid);
        Assert.Equal("shoe.jpg", result.FileName);
        Assert.Equal("image/jpeg", result.ContentType);
    }

    private static IFormFile CreateFile(byte[] bytes, string contentType, string fileName)
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }
}
