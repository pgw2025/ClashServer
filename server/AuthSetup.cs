using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ClashServer.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ClashServer.Web;

/// <summary>管理端 Cookie 认证配置、accessToken 校验与会话绑定。</summary>
public static class AuthSetup
{
    public const string TokenHashClaim = "vn"; // token 摘要 claim

    public static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    /// <summary>注册 Cookie 认证 + 授权，并挂载会话绑定（Token 轮换后旧会话自动失效）。</summary>
    public static void AddManagementAuth(this IServiceCollection services)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax; // CSRF 基础防线：跨站 POST 不带 Cookie
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(30);

                options.Events.OnRedirectToLogin = ctx =>
                {
                    // SPA 场景：/api 未登录一律 401 JSON，绝不 302（登录跳转交给前端守卫）
                    if (ctx.Request.Path.StartsWithSegments("/api"))
                    {
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }
                    ctx.Response.Redirect(ctx.RedirectUri);
                    return Task.CompletedTask;
                };

                // 会话绑定：principal 内记录 token 摘要，Token 变更则吊销会话
                options.Events.OnValidatePrincipal = ctx =>
                {
                    var storage = ctx.HttpContext.RequestServices.GetRequiredService<IStorageService>();
                    var settings = storage.GetSettingsAsync().GetAwaiter().GetResult();
                    var currentHash = string.IsNullOrWhiteSpace(settings.AccessToken)
                        ? string.Empty
                        : HashToken(settings.AccessToken);
                    var principalHash = ctx.Principal?.FindFirst(TokenHashClaim)?.Value;
                    if (!string.IsNullOrEmpty(currentHash) && !string.Equals(currentHash, principalHash, StringComparison.Ordinal))
                    {
                        ctx.RejectPrincipal();
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    }
                    return Task.CompletedTask;
                };
            });
        services.AddAuthorization();
    }

    /// <summary>固定时间比较，防时序侧信道。两边任意一个为空一律视为不匹配。</summary>
    public static bool TokenMatches(string provided, string expected)
    {
        if (string.IsNullOrWhiteSpace(expected)) return false; // service 未配 Token → 拒绝（fail-closed）
        if (string.IsNullOrWhiteSpace(provided)) return false;
        var a = Encoding.UTF8.GetBytes(provided);
        var b = Encoding.UTF8.GetBytes(expected);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    public static ClaimsPrincipal BuildPrincipal(string token)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(TokenHashClaim, HashToken(token)),
            new Claim(ClaimTypes.Name, "admin")
        }, CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }
}

/// <summary>CSRF 纵深防线：/api 写操作必须携带自定义头 X-Requested-With: fetch（跨站表单无法伪造）。</summary>
public class ApiCsrfFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        if (!HttpMethods.IsGet(ctx.HttpContext.Request.Method) &&
            !ctx.HttpContext.Request.Headers.ContainsKey("X-Requested-With"))
        {
            return Results.BadRequest(new { ok = false, error = "缺少 X-Requested-With 头" });
        }
        return await next(ctx);
    }
}