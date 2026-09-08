using ImageCdn.Api.Contracts;
using ImageCdn.Api.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ImageCdn.Api.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    private readonly ImageProviderOptions _providerOptions;

    public HealthController(IOptions<ImageProviderOptions> providerOptions)
    {
        _providerOptions = providerOptions.Value;
    }

    [HttpGet]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public ActionResult<HealthResponse> Get()
    {
        var provider = _providerOptions.IsCloudflare ? "Cloudflare" : "Local";
        return Ok(new HealthResponse("ok", provider));
    }
}
