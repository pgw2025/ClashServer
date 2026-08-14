using System.Text;
using ClashServer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IStorageService, StorageService>();
builder.Services.AddSingleton<IClashSubService, ClashSubService>();
builder.Services.AddHostedService<BackgroundRefreshService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapGet("/sub", async (HttpContext context, IClashSubService subService, IStorageService storage, ILogger<Program> logger) =>
{
    try
    {
        var settings = await storage.GetSettingsAsync();
        var providedToken = context.Request.Query["token"].FirstOrDefault()
                            ?? context.Request.Headers["Authorization"].FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase)
                            ?? context.Request.Headers["X-Access-Token"].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            if (string.IsNullOrWhiteSpace(providedToken) || !string.Equals(providedToken, settings.AccessToken, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unauthorized: Invalid or missing access token.", Encoding.UTF8);
                return;
            }
        }

        var force = context.Request.Query["refresh"].FirstOrDefault() == "1";
        var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
        var yaml = await subService.GetMergedSubAsync(baseUrl, force, context.RequestAborted);

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/html; charset=UTF-8";
        // context.Response.ContentType = "application/x-yaml; charset=utf-8";
        // context.Response.Headers["Content-Disposition"] = "attachment; filename=\"config.yaml\"";
        // context.Response.Headers["Cache-Control"] = "public, max-age=900";
        var yamlBytes = Encoding.UTF8.GetBytes(yaml);
        await context.Response.Body.WriteAsync(yamlBytes);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "/sub 端点处理请求时出错");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync($"Error: {ex.Message}", Encoding.UTF8);
    }
});

app.MapGet("/api/sub-health", async (IClashSubService subService, HttpContext context) =>
{
    var ok = await subService.TestUpstreamAsync(context.RequestAborted);
    context.Response.StatusCode = ok ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
    context.Response.ContentType = "application/json; charset=utf-8";
    await context.Response.WriteAsJsonAsync(new { ok, timestamp = DateTimeOffset.Now });
});

app.MapRazorPages();

app.Run();
