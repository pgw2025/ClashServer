using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ClashServer.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ClashServer.Web;

/// <summary>管理端 Cookie 认证配置、账密校验与会话绑定。</summary>
public static class AuthSetup
{
    public const string CredentialHashClaim = "cred"; // 凭据（用户名|密码哈希）摘要 claim

    private const int PasswordIterations = 210_000;

    public static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    /// <summary>PBKDF2-HMAC-SHA256 加盐哈希，输出格式 pbkdf2-sha256$迭代次数$盐Base64$哈希Base64。</summary>
    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordIterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256${PasswordIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>固定时间比较校验密码；格式非法或参数为空一律视为不匹配。</summary>
    public static bool VerifyPassword(string password, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(storedHash)) return false;
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256"
            || !int.TryParse(parts[1], out var iterations) || iterations <= 0)
        {
            return false;
        }
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>注册 Cookie 认证 + 授权，并挂载会话绑定（改密/改名后旧会话自动失效）。</summary>
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

                // 会话绑定：principal 内记录凭据摘要，改密/改名则吊销会话
                options.Events.OnValidatePrincipal = ctx =>
                {
                    var storage = ctx.HttpContext.RequestServices.GetRequiredService<IStorageService>();
                    var settings = storage.GetSettingsAsync().GetAwaiter().GetResult();
                    var currentHash = string.IsNullOrWhiteSpace(settings.Username) || string.IsNullOrWhiteSpace(settings.PasswordHash)
                        ? string.Empty
                        : HashToken($"{settings.Username}|{settings.PasswordHash}");
                    var principalHash = ctx.Principal?.FindFirst(CredentialHashClaim)?.Value;
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

    public static ClaimsPrincipal BuildPrincipal(string username, string passwordHash)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(CredentialHashClaim, HashToken($"{username}|{passwordHash}")),
            new Claim(ClaimTypes.Name, username)
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