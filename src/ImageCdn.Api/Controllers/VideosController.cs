using ImageCdn.Api.Contracts;
using ImageCdn.Api.Providers.Cloudflare;
using Microsoft.AspNetCore.Mvc;

namespace ImageCdn.Api.Controllers;

[ApiController]
[Route("api/videos")]
[Produces("application/json")]
public sealed class VideosController : ControllerBase
{
    private readonly CloudflareStreamClient _stream;

    public VideosController(CloudflareStreamClient stream)
    {
        _stream = stream;
    }

    /// <summary>
    /// Create a one-time Cloudflare Stream upload URL.
    /// The client uploads the video directly to Cloudflare using the returned uploadUrl.
    /// </summary>
    [HttpPost("direct-upload")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(DirectVideoUploadResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateDirectUpload(
        [FromBody] CreateVideoUploadRequest request,
        CancellationToken cancellationToken)
    {
        if (request.MaxDurationSeconds is < 1 or > 36000)
        {
            return BadRequestProblem("maxDurationSeconds must be between 1 and 36000.");
        }

        if (!string.IsNullOrWhiteSpace(request.Creator) && request.Creator.Length > 64)
        {
            return BadRequestProblem("creator must be 64 characters or fewer.");
        }

        var payload = new CloudflareDirectUploadRequest
        {
            MaxDurationSeconds = request.MaxDurationSeconds,
            Creator = string.IsNullOrWhiteSpace(request.Creator) ? null : request.Creator,
            AllowedOrigins = request.AllowedOrigins is { Count: > 0 } ? request.AllowedOrigins : null,
            RequireSignedUrls = request.RequireSignedUrls,
            Meta = string.IsNullOrWhiteSpace(request.FileName)
                ? null
                : new Dictionary<string, string> { ["name"] = request.FileName }
        };

        var result = await _stream.CreateDirectUploadAsync(payload, cancellationToken);

        if (string.IsNullOrWhiteSpace(result.Uid) || string.IsNullOrWhiteSpace(result.UploadUrl))
        {
            throw new UpstreamProviderException(
                "Cloudflare Stream did not return an upload URL.",
                statusCode: 502);
        }

        return Created(
            $"/api/videos/{result.Uid}",
            new DirectVideoUploadResponse(result.Uid, result.UploadUrl));
    }

    /// <summary>
    /// Get Cloudflare Stream processing state and playback URLs.
    /// </summary>
    [HttpGet("{uid}")]
    [ProducesResponseType(typeof(VideoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        string uid,
        CancellationToken cancellationToken)
    {
        if (!IsValidUid(uid))
        {
            return BadRequestProblem("Invalid video uid.");
        }

        var video = await _stream.GetAsync(uid, cancellationToken);
        if (video is null)
        {
            return NotFoundProblem(uid);
        }

        return Ok(Map(video, uid));
    }

    /// <summary>
    /// Delete a Cloudflare Stream video.
    /// </summary>
    [HttpDelete("{uid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        string uid,
        CancellationToken cancellationToken)
    {
        if (!IsValidUid(uid))
        {
            return BadRequestProblem("Invalid video uid.");
        }

        var deleted = await _stream.DeleteAsync(uid, cancellationToken);
        if (!deleted)
        {
            return NotFoundProblem(uid);
        }

        return NoContent();
    }

    private static VideoResponse Map(CloudflareStreamVideoResult video, string fallbackUid) =>
        new(
            video.Uid ?? fallbackUid,
            video.ReadyToStream,
            video.Preview,
            video.Thumbnail,
            video.Playback is null
                ? null
                : new VideoPlaybackResponse(video.Playback.Hls, video.Playback.Dash),
            video.Status is null
                ? null
                : new VideoStatusResponse(
                    video.Status.State,
                    video.Status.PctComplete,
                    video.Status.ErrorReasonCode,
                    video.Status.ErrorReasonText),
            video.Duration,
            video.Size,
            video.Uploaded,
            video.Creator,
            video.RequireSignedUrls);

    private static bool IsValidUid(string uid) =>
        !string.IsNullOrWhiteSpace(uid)
        && uid.Length <= 64
        && uid.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_');

    private IActionResult BadRequestProblem(string detail) =>
        Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation error",
            detail: detail,
            instance: Request.Path);

    private IActionResult NotFoundProblem(string uid) =>
        Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Video not found",
            detail: $"Video '{uid}' was not found.",
            instance: Request.Path);
}
