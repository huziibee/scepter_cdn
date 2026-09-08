using System.Text.RegularExpressions;
using ImageCdn.Api.Options;
using Microsoft.Extensions.Options;

namespace ImageCdn.Api.Validation;

public sealed class ImagePathValidator
{
    // Letters, numbers, underscore, hyphen, dot, and forward slash only.
    private static readonly Regex AllowedPattern = new(
        @"^[a-zA-Z0-9_\-./]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ImageValidationOptions _options;

    public ImagePathValidator(IOptions<ImageValidationOptions> options)
    {
        _options = options.Value;
    }

    public PathValidationResult Validate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return PathValidationResult.Fail("Path is required.");
        }

        var normalized = Normalize(path);
        if (string.IsNullOrEmpty(normalized))
        {
            return PathValidationResult.Fail("Path cannot be empty after normalization.");
        }

        if (normalized.Length > _options.MaxPathLength)
        {
            return PathValidationResult.Fail($"Path exceeds maximum length of {_options.MaxPathLength} characters.");
        }

        if (normalized.Contains("..", StringComparison.Ordinal))
        {
            return PathValidationResult.Fail("Path must not contain '..'.");
        }

        if (normalized.Contains('\\'))
        {
            return PathValidationResult.Fail("Path must not contain backslashes.");
        }

        if (normalized.Contains('%'))
        {
            return PathValidationResult.Fail("Path must not contain '%' characters.");
        }

        if (normalized.Any(char.IsControl))
        {
            return PathValidationResult.Fail("Path must not contain control characters.");
        }

        if (!AllowedPattern.IsMatch(normalized))
        {
            return PathValidationResult.Fail(
                "Path may only contain letters, numbers, '_', '-', '.', and '/'.");
        }

        if (normalized.StartsWith('/') || normalized.EndsWith('/'))
        {
            return PathValidationResult.Fail("Path must not start or end with '/' after normalization.");
        }

        if (normalized.Contains("//", StringComparison.Ordinal))
        {
            return PathValidationResult.Fail("Path must not contain empty segments.");
        }

        return PathValidationResult.Ok(normalized);
    }

    public static string Normalize(string path)
    {
        return path.Trim().Trim('/');
    }
}

public sealed record PathValidationResult(bool IsValid, string? NormalizedPath, string? Error)
{
    public static PathValidationResult Ok(string normalizedPath) =>
        new(true, normalizedPath, null);

    public static PathValidationResult Fail(string error) =>
        new(false, null, error);
}
