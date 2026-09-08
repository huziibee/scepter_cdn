using ImageCdn.Api.Options;
using ImageCdn.Api.Validation;
using Microsoft.Extensions.Options;

namespace ImageCdn.Api.Tests;

public sealed class ImagePathValidatorTests
{
    private readonly ImagePathValidator _validator = new(Microsoft.Extensions.Options.Options.Create(new ImageValidationOptions()));

    [Theory]
    [InlineData("products/nike/air-max/front")]
    [InlineData("products/coach/tabby-black/01")]
    [InlineData("categories/mens/banner")]
    [InlineData("users/123/avatar")]
    [InlineData("simple")]
    public void Accepts_valid_paths(string path)
    {
        var result = _validator.Validate(path);
        Assert.True(result.IsValid);
        Assert.Equal(path.Trim('/'), result.NormalizedPath);
    }

    [Fact]
    public void Normalizes_leading_and_trailing_slashes()
    {
        var result = _validator.Validate("/products/nike/air-max/front/");
        Assert.True(result.IsValid);
        Assert.Equal("products/nike/air-max/front", result.NormalizedPath);
    }

    [Theory]
    [InlineData("../../secret")]
    [InlineData("foo/../bar")]
    [InlineData(@"foo\bar")]
    [InlineData("foo%2Fbar")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    public void Rejects_invalid_paths(string path)
    {
        var result = _validator.Validate(path);
        Assert.False(result.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void Rejects_overly_long_path()
    {
        var path = new string('a', 1025);
        var result = _validator.Validate(path);
        Assert.False(result.IsValid);
        Assert.Contains("maximum length", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
