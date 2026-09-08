using ImageCdn.Api.Contracts;
using ImageCdn.Api.Options;
using ImageCdn.Api.Providers;
using ImageCdn.Api.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ImageCdn.Api.Controllers;

[ApiController]
[Route("api/images")]
[Produces("application/json")]
public sealed class ImagesController : ControllerBase
{
    private readonly IImageProvider _provider;
    private readonly ImagePathValidator _pathValidator;
    private readonly ImageFileValidator _fileValidator;
    private readonly ImageValidationOptions _validationOptions;

    public ImagesController(
        IImageProvider provider,
        ImagePathValidator pathValidator,
        ImageFileValidator fileValidator,
        IOptions<ImageValidationOptions> validationOptions)
    {
        _provider = provider;
        _pathValidator = pathValidator;
        _fileValidator = fileValidator;
        _validationOptions = validationOptions.Value;
    }

    /// <summary>
    /// Upload a new image with a caller-selected custom path/ID.
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    [ProducesResponseType(typeof(ImageResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> Create(
        [FromForm] CreateImageForm form,
        CancellationToken cancellationToken)
    {
        var pathResult = _pathValidator.Validate(form.Path);
        if (!pathResult.IsValid)
        {
            return ValidationProblem(pathResult.Error!);
        }

        var fileResult = _fileValidator.Validate(form.File);
        if (!fileResult.IsValid)
        {
            return ProblemResult(fileResult.StatusCode, fileResult.Error!);
        }

        await using var stream = form.File!.OpenReadStream();
        var asset = await _provider.UploadAsync(
            stream,
            fileResult.FileName!,
            fileResult.ContentType!,
            pathResult.NormalizedPath!,
            cancellationToken);

        var response = ImageResponseMapper.FromAsset(asset);
        return Created($"/api/images/{asset.Id}", response);
    }

    /// <summary>
    /// Retrieve image metadata for a nested custom path.
    /// </summary>
    [HttpGet("{**path}")]
    [ProducesResponseType(typeof(ImageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string path, CancellationToken cancellationToken)
    {
        var pathResult = _pathValidator.Validate(path);
        if (!pathResult.IsValid)
        {
            return ValidationProblem(pathResult.Error!);
        }

        var asset = await _provider.GetAsync(pathResult.NormalizedPath!, cancellationToken);
        if (asset is null)
        {
            return NotFoundProblem(pathResult.NormalizedPath!);
        }

        return Ok(ImageResponseMapper.FromAsset(asset));
    }

    /// <summary>
    /// Replace image bytes at an existing path. Not fully atomic against Cloudflare.
    /// </summary>
    [HttpPut("{**path}")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    [ProducesResponseType(typeof(ImageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> Replace(
        string path,
        [FromForm] ReplaceImageForm form,
        CancellationToken cancellationToken)
    {
        var pathResult = _pathValidator.Validate(path);
        if (!pathResult.IsValid)
        {
            return ValidationProblem(pathResult.Error!);
        }

        var fileResult = _fileValidator.Validate(form.File);
        if (!fileResult.IsValid)
        {
            return ProblemResult(fileResult.StatusCode, fileResult.Error!);
        }

        await using var stream = form.File!.OpenReadStream();
        var asset = await _provider.ReplaceAsync(
            stream,
            fileResult.FileName!,
            fileResult.ContentType!,
            pathResult.NormalizedPath!,
            cancellationToken);

        return Ok(ImageResponseMapper.FromAsset(asset));
    }

    /// <summary>
    /// Delete an image by nested custom path.
    /// </summary>
    [HttpDelete("{**path}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string path, CancellationToken cancellationToken)
    {
        var pathResult = _pathValidator.Validate(path);
        if (!pathResult.IsValid)
        {
            return ValidationProblem(pathResult.Error!);
        }

        var deleted = await _provider.DeleteAsync(pathResult.NormalizedPath!, cancellationToken);
        if (!deleted)
        {
            return NotFoundProblem(pathResult.NormalizedPath!);
        }

        return NoContent();
    }

    private IActionResult ValidationProblem(string detail) =>
        ProblemResult(StatusCodes.Status400BadRequest, detail, title: "Validation error");

    private IActionResult NotFoundProblem(string path) =>
        ProblemResult(StatusCodes.Status404NotFound, $"Image '{path}' was not found.", title: "Image not found");

    private IActionResult ProblemResult(int statusCode, string detail, string? title = null)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title ?? (statusCode == StatusCodes.Status413PayloadTooLarge ? "Payload too large" : "Bad request"),
            Detail = detail,
            Instance = Request.Path,
            Type = $"https://httpstatuses.com/{statusCode}"
        };

        if (statusCode == StatusCodes.Status413PayloadTooLarge)
        {
            problem.Extensions["maxImageSizeBytes"] = _validationOptions.MaxImageSizeBytes;
        }

        return StatusCode(statusCode, problem);
    }
}
