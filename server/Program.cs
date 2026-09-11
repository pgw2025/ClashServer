using System.Net;
using System.Text;
using ClashServer.Services;
using ClashServer.Web;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IStorageService, StorageService>();
builder.Services.AddSingleton<IClashSubService, ClashSubService>();
builder.Services.AddHostedService<BackgroundRefreshService>();
builder.Services.AddManagementAuth();

var app = builder.Build();

// 反代(nginx)场景：信任本机回环代理的 X-Forwarded-Proto/Host，恢复 https 协议与自定义端口(如 2130)
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
    KnownProxies = { IPAddress.Loopback },
    ForwardLimit = 1
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
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
        var subTimeout = TimeSpan.FromSeconds(settings.PublicSubFetchTimeoutSeconds > 0 ? settings.PublicSubFetchTimeoutSeconds : 10);
        var result = await subService.GetMergedSubSafeAsync(baseUrl, force, context.RequestAborted, subTimeout);

        if (result.Degraded)
        {
            logger.LogWarning("/sub 上游不可达，返回 last-good 降级数据（数据时间: {At}）", result.DataAt);
            context.Response.Headers["X-Cache"] = "stale";
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/html; charset=UTF-8";
        // context.Response.ContentType = "application/x-yaml; charset=utf-8";
        // context.Response.Headers["Content-Disposition"] = "attachment; filename=\"config.yaml\"";
        // context.Response.Headers["Cache-Control"] = "public, max-age=900";
        var yamlBytes = Encoding.UTF8.GetBytes(result.Yaml);
        await context.Response.Body.WriteAsync(yamlBytes);
    }
    catch (Exception ex)
    {
        // R2：上游不可达（含冷启动无 last-good）也必须在 10s 内返回且带 stale 降级，绝不放任 500
        logger.LogError(ex, "/sub 端点处理请求时出错，返回 stale 降级");
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.Headers["X-Cache"] = "stale";
        context.Response.ContentType = "text/html; charset=UTF-8";
        await context.Response.WriteAsync("# 上游订阅不可达，请稍后刷新重试。\n", Encoding.UTF8);
    }
});

app.MapGet("/api/sub-health", async (IClashSubService subService, HttpContext context) =>
{
    var ok = await subService.TestUpstreamAsync(context.RequestAborted);
    context.Response.StatusCode = ok ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
    context.Response.ContentType = "application/json; charset=utf-8";
    await context.Response.WriteAsJsonAsync(new { ok, timestamp = DateTimeOffset.Now });
});

app.MapApi();

// 三联动开关：Vue 模式（SPA fallback + /api 全量鉴权）与旧 Razor 模式互斥，绝不可同时开启。
// 因为 ASP.NET Core 路由大小写不敏感，SPA 路由 /settings 会被 Razor 页面 /Settings 截获，
// 必须通过此开关二选一。
var vueEnabled = app.Configuration.GetValue<bool>("VueApp:Enabled");
if (vueEnabled)
{
    // SPA 模式：MapRazorPages 不启用；把前端路由回退到 index.html，但排除 /api 与 /sub
    app.MapFallback(async (HttpContext ctx) =>
    {
        var path = ctx.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/api") || path.StartsWith("/sub"))
        {
            // 打错的 /api、/sub 路径，返回 404 JSON，绝不能被 fallback 吞成 index.html
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "Not found" });
            return;
        }
        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.SendFileAsync(Path.Combine("wwwroot", "index.html"));
    });
}
else
{
    // 旧 Razor 模式：SPA fallback 不启用；/api 鉴权同步放开（旧页面内联 JS 直接调 /api/nodes 等公开端点）
    app.MapRazorPages();
}

app.Run();
