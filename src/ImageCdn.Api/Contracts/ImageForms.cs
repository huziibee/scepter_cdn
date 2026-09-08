using Microsoft.AspNetCore.Mvc;

namespace ImageCdn.Api.Contracts;

public sealed class CreateImageForm
{
    [FromForm(Name = "file")]
    public IFormFile? File { get; set; }

    [FromForm(Name = "path")]
    public string? Path { get; set; }
}

public sealed class ReplaceImageForm
{
    [FromForm(Name = "file")]
    public IFormFile? File { get; set; }
}
