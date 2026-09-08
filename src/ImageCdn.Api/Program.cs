using System.Net.Http.Headers;
using ImageCdn.Api.Middleware;
using ImageCdn.Api.Options;
using ImageCdn.Api.Providers;
using ImageCdn.Api.Providers.Cloudflare;
using ImageCdn.Api.Providers.Local;
using ImageCdn.Api.Validation;
using Microsoft.Extensions.FileProviders;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

BindEnvironmentOverrides(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddHttpContextAccessor();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Image CDN API",
        Version = "v1",
        Description = "CRUD abstraction over Cloudflare Images (with a Local provider for development)."
    });
    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "Optional API key via X-Api-Key header (required when SERVICE_API_KEY is set).",
        Name = "X-Api-Key",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey
    });
    options.OperationFilter<ImageCdn.Api.OpenApi.ApiKeyOperationFilter>();
});

builder.Services.Configure<ImageProviderOptions>(builder.Configuration.GetSection(ImageProviderOptions.SectionName));
builder.Services.Configure<CloudflareOptions>(builder.Configuration.GetSection(CloudflareOptions.SectionName));
builder.Services.Configure<LocalImageOptions>(builder.Configuration.GetSection(LocalImageOptions.SectionName));
builder.Services.Configure<ImageValidationOptions>(builder.Configuration.GetSection(ImageValidationOptions.SectionName));
builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection(SecurityOptions.SectionName));

builder.Services.AddSingleton<ImagePathValidator>();
builder.Services.AddSingleton<ImageFileValidator>();

var providerName = builder.Configuration.GetValue<string>($"{ImageProviderOptions.SectionName}:Provider")
                   ?? builder.Configuration["IMAGE_PROVIDER"]
                   ?? "Local";

if (string.Equals(providerName, "Cloudflare", StringComparison.OrdinalIgnoreCase))
{
    var cloudflare = builder.Configuration.GetSection(CloudflareOptions.SectionName).Get<CloudflareOptions>()
                     ?? new CloudflareOptions();
    cloudflare.EnsureValid();

    builder.Services.AddHttpClient(CloudflareImagesClient.HttpClientName, (sp, client) =>
    {
        var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CloudflareOptions>>().Value;
        client.BaseAddress = new Uri(opts.ApiBaseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opts.ApiToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    });

    builder.Services.AddSingleton<CloudflareImagesClient>();
    builder.Services.AddSingleton<IImageProvider, CloudflareImageProvider>();
}
else if (string.Equals(providerName, "Local", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IImageProvider, LocalImageProvider>();
}
else
{
    throw new InvalidOperationException(
        $"Unknown IMAGE_PROVIDER '{providerName}'. Supported values: Local, Cloudflare.");
}

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<ApiKeyMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Image CDN API v1");
    });
}

var localOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<LocalImageOptions>>().Value;
var localRoot = Path.GetFullPath(localOptions.Root);
Directory.CreateDirectory(localRoot);

if (string.Equals(providerName, "Local", StringComparison.OrdinalIgnoreCase))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(localRoot),
        RequestPath = localOptions.PublicPathPrefix.TrimEnd('/'),
        ServeUnknownFileTypes = true,
        DefaultContentType = "application/octet-stream",
        OnPrepareResponse = ctx =>
        {
            var metaPath = ctx.File.PhysicalPath + ".meta.json";
            if (!File.Exists(metaPath))
            {
                return;
            }

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(metaPath));
                if (doc.RootElement.TryGetProperty("ContentType", out var ct) ||
                    doc.RootElement.TryGetProperty("contentType", out ct))
                {
                    var value = ct.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        ctx.Context.Response.ContentType = value;
                    }
                }
            }
            catch
            {
                // Keep default content type if meta cannot be read.
            }
        }
    });
}

app.MapControllers();
app.Run();

static void BindEnvironmentOverrides(ConfigurationManager configuration)
{
    var imageProvider = Environment.GetEnvironmentVariable("IMAGE_PROVIDER");
    if (!string.IsNullOrWhiteSpace(imageProvider))
    {
        configuration[$"{ImageProviderOptions.SectionName}:Provider"] = imageProvider;
    }

    SetIfPresent(configuration, "CLOUDFLARE_ACCOUNT_ID", $"{CloudflareOptions.SectionName}:AccountId");
    SetIfPresent(configuration, "CLOUDFLARE_ACCOUNT_HASH", $"{CloudflareOptions.SectionName}:AccountHash");
    SetIfPresent(configuration, "CLOUDFLARE_API_TOKEN", $"{CloudflareOptions.SectionName}:ApiToken");
    SetIfPresent(configuration, "CLOUDFLARE_VARIANT", $"{CloudflareOptions.SectionName}:Variant");
    SetIfPresent(configuration, "LOCAL_IMAGE_ROOT", $"{LocalImageOptions.SectionName}:Root");
    SetIfPresent(configuration, "SERVICE_API_KEY", $"{SecurityOptions.SectionName}:ServiceApiKey");

    var maxSize = Environment.GetEnvironmentVariable("MAX_IMAGE_SIZE_BYTES");
    if (!string.IsNullOrWhiteSpace(maxSize))
    {
        configuration[$"{ImageValidationOptions.SectionName}:MaxImageSizeBytes"] = maxSize;
    }
}

static void SetIfPresent(ConfigurationManager configuration, string envName, string configKey)
{
    var value = Environment.GetEnvironmentVariable(envName);
    if (!string.IsNullOrWhiteSpace(value))
    {
        configuration[configKey] = value;
    }
}

public partial class Program;
