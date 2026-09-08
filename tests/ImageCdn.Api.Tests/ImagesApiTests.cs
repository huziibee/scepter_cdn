using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ImageCdn.Api.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ImageCdn.Api.Tests;

public sealed class ImagesApiTests : IAsyncLifetime
{
    private readonly string _root;
    private readonly WebApplicationFactory<Program> _factory;

    public ImagesApiTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "image-cdn-api-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ImageProvider:Provider"] = "Local",
                    ["LocalImages:Root"] = _root,
                    ["Security:ServiceApiKey"] = "",
                    ["IMAGE_PROVIDER"] = "Local"
                });
            });
        });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public async Task Health_returns_local_provider()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(body);
        Assert.Equal("ok", body.Status);
        Assert.Equal("Local", body.Provider);
    }

    [Fact]
    public async Task Post_get_put_delete_lifecycle()
    {
        var client = _factory.CreateClient();
        var path = "products/nike/air-max/front";

        using (var form = CreateJpegForm("shoe.jpg", path))
        {
            var create = await client.PostAsync("/api/images", form);
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            var created = await create.Content.ReadFromJsonAsync<ImageResponse>();
            Assert.NotNull(created);
            Assert.Equal(path, created.Id);
            Assert.Contains("/local-images/" + path, created.Url);
            Assert.Equal("shoe.jpg", created.Filename);
            Assert.Equal("image/jpeg", created.ContentType);
        }

        var get = await client.GetAsync($"/api/images/{path}");
        get.EnsureSuccessStatusCode();
        var fetched = await get.Content.ReadFromJsonAsync<ImageResponse>();
        Assert.NotNull(fetched);
        Assert.Equal(path, fetched.Id);

        using (var replaceForm = new MultipartFormDataContent())
        {
            var bytes = CreateMinimalJpegBytes();
            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            replaceForm.Add(fileContent, "file", "shoe-v2.png");

            var put = await client.PutAsync($"/api/images/{path}", replaceForm);
            put.EnsureSuccessStatusCode();
            var replaced = await put.Content.ReadFromJsonAsync<ImageResponse>();
            Assert.NotNull(replaced);
            Assert.Equal("shoe-v2.png", replaced.Filename);
        }

        var delete = await client.DeleteAsync($"/api/images/{path}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var missing = await client.GetAsync($"/api/images/{path}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Post_duplicate_path_returns_conflict()
    {
        var client = _factory.CreateClient();
        var path = "products/coach/tabby-black/01";

        using (var form = CreateJpegForm("a.jpg", path))
        {
            var first = await client.PostAsync("/api/images", form);
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        }

        using (var form = CreateJpegForm("b.jpg", path))
        {
            var second = await client.PostAsync("/api/images", form);
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        }
    }

    [Fact]
    public async Task Get_nonexistent_returns_404()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/images/does/not/exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_nonexistent_returns_404()
    {
        var client = _factory.CreateClient();
        var response = await client.DeleteAsync("/api/images/does/not/exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_missing_file_returns_400()
    {
        var client = _factory.CreateClient();
        using var form = new MultipartFormDataContent
        {
            { new StringContent("products/x"), "path" }
        };
        var response = await client.PostAsync("/api/images", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static MultipartFormDataContent CreateJpegForm(string fileName, string path)
    {
        var form = new MultipartFormDataContent();
        var bytes = CreateMinimalJpegBytes();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(fileContent, "file", fileName);
        form.Add(new StringContent(path, Encoding.UTF8), "path");
        return form;
    }

    private static byte[] CreateMinimalJpegBytes() =>
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01,
        0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9
    ];
}
