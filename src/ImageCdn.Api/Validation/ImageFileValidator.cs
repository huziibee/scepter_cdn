using ImageCdn.Api.Options;
using Microsoft.Extensions.Options;

namespace ImageCdn.Api.Validation;

public sealed class ImageFileValidator
{
    private readonly ImageValidationOptions _options;
    private readonly HashSet<string> _allowedContentTypes;

    public ImageFileValidator(IOptions<ImageValidationOptions> options)
    {
        _options = options.Value;
        _allowedContentTypes = new HashSet<string>(
            _options.AllowedContentTypes,
            StringComparer.OrdinalIgnoreCase);
    }

    public FileValidationResult Validate(IFormFile? file)
    {
        if (file is null)
        {
            return FileValidationResult.Fail("File is required.", StatusCodes.Status400BadRequest);
        }

        if (file.Length <= 0)
        {
            return FileValidationResult.Fail("File must not be empty.", StatusCodes.Status400BadRequest);
        }

        if (file.Length > _options.MaxImageSizeBytes)
        {
            return FileValidationResult.Fail(
                $"File exceeds maximum size of {_options.MaxImageSizeBytes} bytes.",
                StatusCodes.Status413PayloadTooLarge);
        }

        var contentType = string.IsNullOrWhiteSpace(file.ContentType)
            ? "application/octet-stream"
            : file.ContentType.Split(';', 2)[0].Trim();

        if (!_allowedContentTypes.Contains(contentType))
        {
            return FileValidationResult.Fail(
                $"Unsupported content type '{contentType}'. Allowed: {string.Join(", ", _options.AllowedContentTypes)}.",
                StatusCodes.Status400BadRequest);
        }

        var fileName = string.IsNullOrWhiteSpace(file.FileName) ? "upload.bin" : Path.GetFileName(file.FileName);
        return FileValidationResult.Ok(fileName, contentType);
    }
}

public sealed record FileValidationResult(
    bool IsValid,
    string? FileName,
    string? ContentType,
    string? Error,
    int StatusCode)
{
    public static FileValidationResult Ok(string fileName, string contentType) =>
        new(true, fileName, contentType, null, StatusCodes.Status200OK);

    public static FileValidationResult Fail(string error, int statusCode) =>
        new(false, null, null, error, statusCode);
}
