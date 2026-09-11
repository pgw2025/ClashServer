using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClashServer.Models;
using ClashServer.Models.Dtos;
using ClashServer.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace ClashServer.Web;

/// <summary>
/// 管理端 REST API。业务数据全部来自 last-good 快照/直接落盘，
/// 保证上游不可达时接口仍在超时窗口内返回（R1），并对列表响应附 stale 元数据（R5）。
/// </summary>
public static class ApiEndpoints
{
    public static void MapApi(this WebApplication app)
    {
        // /api/sub-health 由 Program.cs 单独注册，保持无鉴权（健康探针，与 /sub 同理隔离于登录）
        // /api 鉴权三联动：仅 Vue 模式启用；旧 Razor 模式放开（旧页面内联 JS 匿名拉 /api/nodes 等）
        var requireAuth = app.Configuration.GetValue<bool>("VueApp:Enabled");
        var group = app.MapGroup("/api")
            .AddEndpointFilter<ApiCsrfFilter>();
        if (requireAuth) group = group.RequireAuthorization();

        // 登录/登出/状态：独立匿名组（置于 /api 之外，避免被 RequireAuthorization 拦截）
        MapAuth(app.MapGroup("/api/auth"));

        MapRules(group);
        MapSettings(group);
        MapConfig(group);
        MapNodes(group);
        MapDashboard(group);
        MapBackup(group);
    }

    private static void MapAuth(RouteGroupBuilder g)
    {
        g.MapPost("/login", async (HttpContext ctx) =>
        {
            var (req, badJson) = await ReadJson<LoginRequest>(ctx.Request.Body);
            if (badJson || req == null)
                return Results.Json(ApiResponse<object>.Failure("请求体无效"), statusCode: 400);
            var settings = await storageGet(ctx.RequestServices);
            // fail-closed：未配置管理员账号时一律拒绝登录
            if (string.IsNullOrWhiteSpace(settings.Username) || string.IsNullOrWhiteSpace(settings.PasswordHash))
                return Results.Json(ApiResponse<object>.Failure("登录失败：服务器未配置管理员账号"), statusCode: 401);

            // 简单限速：同一来源(IP)连续失败 5 次锁定 30 秒
            var cache = ctx.RequestServices.GetRequiredService<IMemoryCache>();
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var lockKey = $"login-lock:{ip}";
            var failKey = $"login-fail:{ip}";
            if (cache.TryGetValue(lockKey, out _))
                return Results.Json(ApiResponse<object>.Failure("尝试次数过多，请稍后重试"), statusCode: 429);

            var usernameOk = !string.IsNullOrWhiteSpace(req.Username)
                && CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(req.Username.Trim()),
                    Encoding.UTF8.GetBytes(settings.Username));
            var passwordOk = AuthSetup.VerifyPassword(req.Password ?? string.Empty, settings.PasswordHash);

            if (!usernameOk || !passwordOk)
            {
                // 统一文案，不区分具体原因，防用户名枚举
                var fails = 1;
                if (cache.TryGetValue(failKey, out int current)) fails = current + 1;
                cache.Set(failKey, fails, TimeSpan.FromMinutes(5));
                if (fails >= 5)
                {
                    cache.Remove(failKey);
                    cache.Set(lockKey, true, TimeSpan.FromSeconds(30));
                }
                return Results.Json(ApiResponse<object>.Failure("登录失败：用户名或密码错误"), statusCode: 401);
            }

            cache.Remove(failKey);
            var principal = AuthSetup.BuildPrincipal(settings.Username, settings.PasswordHash);
            await ctx.SignInAsync(principal, new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30)
            });
            return Results.Ok(ApiResponse<object>.Success(new { loggedIn = true }));
        });

        g.MapGet("/status", (HttpContext ctx) =>
        {
            var loggedIn = ctx.User.Identity?.IsAuthenticated == true;
            return Results.Ok(ApiResponse<object>.Success(new { loggedIn }));
        });

        g.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync();
            return Results.Ok(ApiResponse<object>.Success(new { loggedOut = true }));
        });
    }

    private static void MapRules(RouteGroupBuilder g)
    {
        g.MapGet("/rules", async (IStorageService storage, IClashSubService sub) =>
        {
            var rules = (await storage.GetRulesAsync())
                .Select(RuleDto.From).ToList();
            var stale = ComputeStale(sub, await storage.GetSettingsAsync());
            return Results.Ok(ApiResponse<List<RuleDto>>.Success(rules, stale.LastGood, stale.IsStale));
        });

        g.MapPost("/rules", async ([FromBody] RuleDto dto, IStorageService storage, IClashSubService sub) =>
        {
            if (!IsRuleValid(dto, out var error)) return Results.BadRequest(ApiResponse<object>.Failure(error));
            var rules = await storage.GetRulesAsync();
            var rule = dto.ToRule();
            rule.Id = Guid.NewGuid();
            rule.UpdatedAt = DateTime.Now;
            rules.Insert(0, rule);
            await storage.SaveRulesAsync(rules);
            sub.ClearCache();
            return Results.Ok(ApiResponse<RuleDto>.Success(RuleDto.From(rule)));
        });

        g.MapPut("/rules/{id}", async (Guid id, [FromBody] RuleDto dto, IStorageService storage, IClashSubService sub) =>
        {
            var rules = await storage.GetRulesAsync();
            var idx = rules.FindIndex(r => r.Id == id);
            if (idx < 0) return Results.NotFound(ApiResponse<object>.Failure("规则不存在"));
            if (!IsRuleValid(dto, out var error)) return Results.BadRequest(ApiResponse<object>.Failure(error));
            var rule = dto.ToRule();
            rule.Id = id;
            rule.UpdatedAt = DateTime.Now;
            rules[idx] = rule;
            await storage.SaveRulesAsync(rules);
            sub.ClearCache();
            return Results.Ok(ApiResponse<RuleDto>.Success(RuleDto.From(rule)));
        });

        g.MapDelete("/rules/{id}", async (Guid id, IStorageService storage, IClashSubService sub) =>
        {
            var rules = await storage.GetRulesAsync();
            var removed = rules.RemoveAll(r => r.Id == id);
            await storage.SaveRulesAsync(rules);
            sub.ClearCache();
            return removed > 0
                ? Results.Ok(ApiResponse<object>.Success(new { removed }))
                : Results.NotFound(ApiResponse<object>.Failure("规则不存在"));
        });

        g.MapPost("/rules/{id}/toggle", async (Guid id, IStorageService storage, IClashSubService sub) =>
        {
            var rules = await storage.GetRulesAsync();
            var rule = rules.FirstOrDefault(r => r.Id == id);
            if (rule == null) return Results.NotFound(ApiResponse<object>.Failure("规则不存在"));
            rule.Enabled = !rule.Enabled;
            rule.UpdatedAt = DateTime.Now;
            await storage.SaveRulesAsync(rules);
            sub.ClearCache();
            return Results.Ok(ApiResponse<RuleDto>.Success(RuleDto.From(rule)));
        });

        g.MapPost("/rules/{id}/move-up", async (Guid id, IStorageService storage, IClashSubService sub) =>
        {
            var rules = await storage.GetRulesAsync();
            var idx = rules.FindIndex(r => r.Id == id);
            if (idx > 0)
            {
                (rules[idx - 1], rules[idx]) = (rules[idx], rules[idx - 1]);
                rules[idx].UpdatedAt = DateTime.Now;
                rules[idx - 1].UpdatedAt = DateTime.Now;
                await storage.SaveRulesAsync(rules);
                sub.ClearCache();
            }
            return Results.Ok(ApiResponse<object>.Success(new { moved = idx > 0 }));
        });

        g.MapPost("/rules/{id}/move-down", async (Guid id, IStorageService storage, IClashSubService sub) =>
        {
            var rules = await storage.GetRulesAsync();
            var idx = rules.FindIndex(r => r.Id == id);
            var moved = idx >= 0 && idx < rules.Count - 1;
            if (moved)
            {
                (rules[idx + 1], rules[idx]) = (rules[idx], rules[idx + 1]);
                rules[idx].UpdatedAt = DateTime.Now;
                rules[idx + 1].UpdatedAt = DateTime.Now;
                await storage.SaveRulesAsync(rules);
                sub.ClearCache();
            }
            return Results.Ok(ApiResponse<object>.Success(new { moved }));
        });

        g.MapPost("/rules/batch", async ([FromBody] BatchRequest req, IStorageService storage, IClashSubService sub) =>
        {
            var ids = req.Ids?.ToHashSet() ?? new HashSet<Guid>();
            var rules = await storage.GetRulesAsync();
            int count = 0;
            foreach (var r in rules)
            {
                if (ids.Contains(r.Id))
                {
                    r.Policy = req.Policy ?? r.Policy;
                    r.UpdatedAt = DateTime.Now;
                    count++;
                }
            }
            await storage.SaveRulesAsync(rules);
            sub.ClearCache();
            return Results.Ok(ApiResponse<object>.Success(new { changed = count }));
        });

        g.MapPost("/rules/clear", async (IStorageService storage, IClashSubService sub) =>
        {
            await storage.SaveRulesAsync(new List<CustomRule>());
            sub.ClearCache();
            return Results.Ok(ApiResponse<object>.Success(new { cleared = true }));
        });

        // 解析 YAML 规则文本，返回预览（供 SPA 导入页调用；解析逻辑复用 RuleParser）
        g.MapPost("/rules/parse", ([FromBody] ParseRequest req) =>
        {
            var rules = RuleParser.ParseText(req?.Yaml ?? string.Empty);
            return Results.Ok(ApiResponse<List<CustomRule>>.Success(rules));
        });

        // 导入已确认的规则列表，批量插入并刷新缓存
        g.MapPost("/rules/import", async ([FromBody] ImportRequest req, IStorageService storage, IClashSubService sub) =>
        {
            if (req?.Rules == null || req.Rules.Count == 0)
                return Results.BadRequest(ApiResponse<object>.Failure("没有可导入的规则"));
            var existing = await storage.GetRulesAsync();
            var added = 0;
            foreach (var r in req.Rules)
            {
                r.Id = Guid.NewGuid();
                r.UpdatedAt = DateTime.Now;
                existing.Insert(0, r);
                added++;
            }
            await storage.SaveRulesAsync(existing);
            sub.ClearCache();
            return Results.Ok(ApiResponse<object>.Success(new { imported = added }));
        });
    }

    private static void MapSettings(RouteGroupBuilder g)
    {
        g.MapGet("/settings", async (IStorageService storage) =>
        {
            var settings = await storage.GetSettingsAsync();
            return Results.Ok(ApiResponse<SettingsDto>.Success(SettingsDto.From(settings)));
        });

        g.MapPut("/settings", async ([FromBody] SettingsDto dto, IStorageService storage, IClashSubService sub) =>
        {
            var existing = await storage.GetSettingsAsync();
            var settings = new AppSettings
            {
                UpstreamUrl = dto.UpstreamUrl,
                AccessToken = dto.AccessToken,
                Username = string.IsNullOrWhiteSpace(dto.Username) ? existing.Username : dto.Username.Trim(),
                PasswordHash = existing.PasswordHash, // 密码不走通用保存，仅通过 /api/settings/password 修改
                CacheMinutes = dto.CacheMinutes,
                AdminFetchTimeoutSeconds = dto.AdminFetchTimeoutSeconds,
                PublicSubFetchTimeoutSeconds = dto.PublicSubFetchTimeoutSeconds,
                InsertRulesBefore = dto.InsertRulesBefore,
                ReplaceMode = dto.ReplaceMode,
                AutoGroupNodes = dto.AutoGroupNodes,
                UpdatedAt = DateTime.Now
            };
            await storage.SaveSettingsAsync(settings);
            sub.ClearCache();
            return Results.Ok(ApiResponse<SettingsDto>.Success(SettingsDto.From(settings)));
        });

        g.MapPost("/settings/password", async ([FromBody] ChangePasswordRequest req, IStorageService storage, HttpContext ctx) =>
        {
            var settings = await storage.GetSettingsAsync();
            if (string.IsNullOrWhiteSpace(settings.Username) || string.IsNullOrWhiteSpace(settings.PasswordHash))
                return Results.Json(ApiResponse<object>.Failure("服务器未配置管理员账号"), statusCode: 401);
            if (string.IsNullOrWhiteSpace(req?.CurrentPassword) || !AuthSetup.VerifyPassword(req.CurrentPassword, settings.PasswordHash))
                return Results.Json(ApiResponse<object>.Failure("当前密码错误"), statusCode: 400);
            if (string.IsNullOrWhiteSpace(req?.NewPassword) || req.NewPassword.Length < 8 || req.NewPassword.Length > 64)
                return Results.Json(ApiResponse<object>.Failure("新密码长度应为 8-64 个字符"), statusCode: 400);

            settings.PasswordHash = AuthSetup.HashPassword(req.NewPassword);
            await storage.SaveSettingsAsync(settings);

            // 用新凭据摘要重新签发当前会话 Cookie（改密者不掉线），其他会话下一次请求自动 401
            if (ctx.User.Identity?.IsAuthenticated == true)
            {
                await ctx.SignInAsync(AuthSetup.BuildPrincipal(settings.Username, settings.PasswordHash), new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30)
                });
            }
            return Results.Ok(ApiResponse<object>.Success(new { changed = true }));
        });

        g.MapPost("/settings/generate-token", () =>
        {
            var token = Guid.NewGuid().ToString("N")[..16];
            return Results.Ok(ApiResponse<object>.Success(new { token }));
        });
    }

    private static void MapConfig(RouteGroupBuilder g)
    {
        g.MapGet("/config/raw", async (IClashSubService sub, IStorageService storage, HttpContext ctx) =>
        {
            var settings = await storage.GetSettingsAsync();
            var timeout = TimeSpan.FromSeconds(settings.AdminFetchTimeoutSeconds > 0 ? settings.AdminFetchTimeoutSeconds : 5);
            var stale = ComputeStale(sub, settings);
            try
            {
                var yaml = await sub.GetRawUpstreamYamlAsync(ct: ctx.RequestAborted, fetchTimeout: timeout);
                var dto = BuildConfigDto(yaml, sub, settings);
                return Results.Ok(ApiResponse<ConfigDto>.Success(dto, dto.LastGoodUpdate, dto.IsStale));
            }
            catch
            {
                // R1：冷启动且上游失败时返回空配置并标记 stale，绝不放任 500
                return Results.Ok(ApiResponse<ConfigDto>.Success(BuildConfigDto(string.Empty, sub, settings), stale.LastGood, true));
            }
        });

        g.MapGet("/config/merged", async (IClashSubService sub, IStorageService storage, HttpContext ctx) =>
        {
            var settings = await storage.GetSettingsAsync();
            var timeout = TimeSpan.FromSeconds(settings.AdminFetchTimeoutSeconds > 0 ? settings.AdminFetchTimeoutSeconds : 5);
            var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            var stale = ComputeStale(sub, settings);
            try
            {
                // 用 Safe 版本：有 last-good 时自动降级；冷启动无快照则下方 catch 兜底为空配置
                var r = await sub.GetMergedSubSafeAsync(baseUrl, ct: ctx.RequestAborted, fetchTimeout: timeout);
                var dto = BuildConfigDto(r.Yaml, sub, settings);
                return Results.Ok(ApiResponse<ConfigDto>.Success(dto, dto.LastGoodUpdate, dto.IsStale));
            }
            catch
            {
                // R1：冷启动且上游失败时返回空配置并标记 stale，绝不放任 500
                return Results.Ok(ApiResponse<ConfigDto>.Success(BuildConfigDto(string.Empty, sub, settings), stale.LastGood, true));
            }
        });

        g.MapPost("/config/refresh", async (IClashSubService sub, HttpContext ctx) =>
        {
            try
            {
                var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
                await sub.GetRawUpstreamYamlAsync(forceRefresh: true, ct: ctx.RequestAborted, fetchTimeout: TimeSpan.FromSeconds(10));
                await sub.GetMergedSubAsync(baseUrl, forceRefresh: true, ct: ctx.RequestAborted, fetchTimeout: TimeSpan.FromSeconds(10));
                return Results.Ok(ApiResponse<object>.Success(new { refreshed = true }));
            }
            catch (Exception ex)
            {
                return Results.Json(ApiResponse<object>.Failure(ex.Message), statusCode: 500);
            }
        });
    }

    private static void MapNodes(RouteGroupBuilder g)
    {
        // 覆盖式重定义既有 /api/nodes、/api/groups，补充 stale 元数据
        g.MapGet("/nodes", async (IClashSubService sub, IStorageService storage, HttpContext ctx) =>
        {
            try
            {
                var settings = await storage.GetSettingsAsync();
                var timeout = TimeSpan.FromSeconds(settings.AdminFetchTimeoutSeconds > 0 ? settings.AdminFetchTimeoutSeconds : 5);
                var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
                var nodes = await sub.GetProxyNodesAsync(baseUrl, false, ctx.RequestAborted, timeout);
                var stale = ComputeStale(sub, settings);
                return Results.Ok(ApiResponse<List<ProxyNode>>.Success(nodes, stale.LastGood, stale.IsStale));
            }
            catch
            {
                // R1：上游不可达时不抛 500，回退到内存缓存（无数据则空数组），附 stale 元数据
                var cached = sub.GetCachedNodes() ?? new List<ProxyNode>();
                var settings = await storage.GetSettingsAsync();
                var stale = ComputeStale(sub, settings);
                return Results.Ok(ApiResponse<List<ProxyNode>>.Success(cached, stale.LastGood, true));
            }
        });

        g.MapGet("/groups", async (IClashSubService sub, IStorageService storage) =>
        {
            try
            {
                var settings = await storage.GetSettingsAsync();
                var timeout = TimeSpan.FromSeconds(settings.AdminFetchTimeoutSeconds > 0 ? settings.AdminFetchTimeoutSeconds : 5);
                var groups = await sub.GetProxyGroupsAsync(false, ct: default, fetchTimeout: timeout);
                var stale = ComputeStale(sub, settings);
                return Results.Ok(ApiResponse<List<string>>.Success(groups, stale.LastGood, stale.IsStale));
            }
            catch
            {
                // R1：上游不可达时回退缓存，group 名无缓存则空数组
                var cached = sub.GetCachedGroups() ?? new List<string>();
                var settings = await storage.GetSettingsAsync();
                var stale = ComputeStale(sub, settings);
                return Results.Ok(ApiResponse<List<string>>.Success(cached, stale.LastGood, true));
            }
        });

        // 单节点测速：节点名可能含中文/空格/斜杠，不能作 URL 路径参数，改用 body 传 name
        g.MapPost("/nodes/latency", async ([FromBody] LatencyRequest req, IClashSubService sub, IStorageService storage, HttpContext ctx) =>
        {
            if (string.IsNullOrWhiteSpace(req?.Name)) return Results.BadRequest(ApiResponse<object>.Failure("name 不能为空"));
            var settings = await storage.GetSettingsAsync();
            var timeout = TimeSpan.FromSeconds(settings.AdminFetchTimeoutSeconds > 0 ? settings.AdminFetchTimeoutSeconds : 5);
            var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            var nodes = await sub.GetProxyNodesAsync(baseUrl, ct: ctx.RequestAborted, fetchTimeout: timeout);
            var node = nodes.FirstOrDefault(n => n.Name == req.Name);
            if (node == null) return Results.NotFound(ApiResponse<object>.Failure("未找到该节点"));

            var result = await sub.TestNodeLatencyAsync(node, ctx.RequestAborted);
            return Results.Ok(ApiResponse<object>.Success(new
            {
                ok = result.Error == null,
                name = result.Name,
                latency = result.Latency,
                error = result.Error
            }));
        });
    }

    private static void MapDashboard(RouteGroupBuilder g)
    {
        g.MapGet("/dashboard", async (IClashSubService sub, IStorageService storage, HttpContext ctx) =>
        {
            var settings = await storage.GetSettingsAsync();
            var rules = await storage.GetRulesAsync();
            var stale = ComputeStale(sub, settings);

            var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            var subUrl = $"{baseUrl}/sub";
            if (!string.IsNullOrWhiteSpace(settings.AccessToken))
                subUrl += $"?token={Uri.EscapeDataString(settings.AccessToken)}";

            var nodes = sub.GetCachedNodes();
            var dto = new DashboardDto
            {
                SubUrl = subUrl,
                EnabledRuleCount = rules.Count(r => r.Enabled),
                NodeCount = nodes?.Count ?? 0,
                LastGoodUpdate = stale.LastGood,
                IsStale = stale.IsStale
            };
            return Results.Ok(ApiResponse<DashboardDto>.Success(dto, stale.LastGood, stale.IsStale));
        });

        // 手动强制刷新缓存（仪表盘「立即刷新」）
        g.MapPost("/cache/refresh", async (IClashSubService sub, IStorageService storage, HttpContext ctx) =>
        {
            try
            {
                var settings = await storage.GetSettingsAsync();
                var timeout = TimeSpan.FromSeconds(settings.PublicSubFetchTimeoutSeconds > 0 ? settings.PublicSubFetchTimeoutSeconds : 10);
                var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
                await sub.GetMergedSubAsync(baseUrl, forceRefresh: true, ct: ctx.RequestAborted, fetchTimeout: timeout);
                await sub.GetProxyNodesAsync(baseUrl, forceRefresh: true, ct: ctx.RequestAborted, fetchTimeout: timeout);
                return Results.Ok(ApiResponse<object>.Success(new { refreshed = true }));
            }
            catch (Exception ex)
            {
                return Results.Json(ApiResponse<object>.Failure(ex.Message), statusCode: 500);
            }
        });
    }

    private const int BackupFormatVersion = 1;

    private static void MapBackup(RouteGroupBuilder g)
    {
        // 导出：settings+rules+manifest 打包为 zip，纯本地读取，不依赖上游（R1 不受影响）
        g.MapGet("/backup/export", async (IStorageService storage) =>
        {
            var settingsBytes = await storage.GetRawSettingsFileAsync();
            var rulesBytes = await storage.GetRawRulesFileAsync();
            var manifest = new BackupManifest
            {
                FormatVersion = BackupFormatVersion,
                ExportedAt = DateTimeOffset.Now,
                Entries = new[] { "manifest.json", "settings.json", "rules.json" }.ToList()
            };

            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                await WriteZipEntryAsync(zip, "manifest.json",
                    JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions));
                await WriteZipEntryAsync(zip, "settings.json", settingsBytes);
                await WriteZipEntryAsync(zip, "rules.json", rulesBytes);
            }
            ms.Position = 0;

            var fileName = $"clashserver-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
            return Results.File(ms.ToArray(), "application/zip", fileName);
        });

        // 预览：只读校验并返回清单，绝不在磁盘落任何内容
        // 不走 IFormFile 绑定——那会触发 .NET 8 对 form 端点的自动 antiforgery（需 UseAntiforgery 中间件）。
        // 从 HttpContext 手读表单文件，CSRF 仍由自定义 ApiCsrfFilter（强制 X-Requested-With 头）兜底。
        g.MapPost("/backup/preview", async (HttpContext ctx) =>
        {
            var file = ctx.Request.Form.Files.GetFile("file");
            return await HandlePreviewOrImportAsync(file, commit: false, storage: null!, sub: null!);
        });

        // 导入：校验通过后「自动备份当前数据 → 全量覆盖 → 清缓存」，返回还原摘要
        g.MapPost("/backup/import", async (HttpContext ctx, IStorageService storage, IClashSubService sub) =>
        {
            var file = ctx.Request.Form.Files.GetFile("file");
            return await HandlePreviewOrImportAsync(file, commit: true, storage, sub);
        });
    }

    // 由 preview / import 复用：先只读校验 zip；commit=true 时才执行还原落盘
    private static async Task<IResult> HandlePreviewOrImportAsync(
        IFormFile? file, bool commit, IStorageService storage, IClashSubService sub)
    {
        if (file == null || file.Length == 0)
            return Results.BadRequest(ApiResponse<object>.Failure("请选择备份文件"));

        var (files, badZip) = await ReadZipAsync(file);
        if (badZip || files.Count == 0)
            return Results.BadRequest(ApiResponse<object>.Failure("无效的 zip 备份文件"));

        var (manifest, settings, rules, error) = await ValidateBackupAsync(files);
        if (error != null)
            return Results.BadRequest(ApiResponse<object>.Failure(error));

        if (!commit)
        {
            return Results.Ok(ApiResponse<BackupPreviewResponse>.Success(new BackupPreviewResponse
            {
                ExportedAt = manifest!.ExportedAt,
                FormatVersion = manifest.FormatVersion,
                ContainsSettings = settings != null,
                RuleCount = rules!.Count,
                Entries = manifest.Entries ?? new List<string>()
            }));
        }

        // 写盘前读取当前凭据，判定还原后是否需要重新登录（凭据哈希绑定）
        var current = await storage.GetSettingsAsync();
        var credChanged = settings != null
            && (!string.Equals(settings.Username?.Trim(), current.Username?.Trim(), StringComparison.Ordinal)
                || !string.Equals(settings.PasswordHash, current.PasswordHash, StringComparison.Ordinal));

        var backupDir = await storage.SnapshotBackupAsync();

        var restored = new List<string>();
        if (settings != null)
        {
            await storage.WriteRawSettingsFileAsync(files["settings.json"]);
            restored.Add("settings.json");
        }
        if (rules != null)
        {
            await storage.WriteRawRulesFileAsync(files["rules.json"]);
            restored.Add("rules.json");
        }
        sub.ClearCache();

        return Results.Ok(ApiResponse<BackupImportResponse>.Success(new BackupImportResponse
        {
            ExportedAt = manifest!.ExportedAt,
            FormatVersion = manifest.FormatVersion,
            RuleCount = rules!.Count,
            RestoredEntries = restored,
            BackupDir = backupDir,
            CredentialsChanged = credChanged
        }));
    }

    private static async Task WriteZipEntryAsync(ZipArchive zip, string name, byte[] content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var s = entry.Open();
        await s.WriteAsync(content);
    }

    private static async Task<(Dictionary<string, byte[]> Files, bool BadZip)> ReadZipAsync(IFormFile file)
    {
        var map = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            ms.Position = 0;
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            foreach (var e in zip.Entries)
            {
                if (e.Name.Length == 0) continue; // 跳过目录条目
                await using var s = e.Open();
                using var buf = new MemoryStream();
                await s.CopyToAsync(buf);
                map[e.FullName] = buf.ToArray();
            }
            return (map, false);
        }
        catch (InvalidDataException)
        {
            return (map, true);
        }
    }

    private static async Task<(BackupManifest? Manifest, AppSettings? Settings, List<CustomRule>? Rules, string? Error)>
        ValidateBackupAsync(Dictionary<string, byte[]> files)
    {
        BackupManifest? manifest;
        if (!files.TryGetValue("manifest.json", out var mj))
            return (null, null, null, "备份缺少 manifest.json");
        try
        {
            manifest = JsonSerializer.Deserialize<BackupManifest>(mj, JsonOptions);
        }
        catch (JsonException)
        {
            return (null, null, null, "manifest.json 内容损坏");
        }
        if (manifest == null)
            return (null, null, null, "manifest.json 为空");
        if (manifest.FormatVersion != BackupFormatVersion)
            return (null, null, null, $"不支持的备份格式版本：{manifest.FormatVersion}");

        AppSettings? settings = null;
        List<CustomRule>? rules = null;
        if (files.TryGetValue("settings.json", out var sj))
        {
            try { settings = JsonSerializer.Deserialize<AppSettings>(sj, JsonOptions); }
            catch (JsonException) { return (null, null, null, "settings.json 内容损坏"); }
        }
        if (files.TryGetValue("rules.json", out var rj))
        {
            try { rules = JsonSerializer.Deserialize<List<CustomRule>>(rj, JsonOptions); }
            catch (JsonException) { return (null, null, null, "rules.json 内容损坏"); }
        }
        else
        {
            rules = new List<CustomRule>();
        }

        foreach (var r in rules!)
        {
            if (!IsRuleValid(RuleDto.From(r), out var err))
                return (null, null, null, $"规则「{r.Target}」无效：{err}");
        }
        return (manifest, settings, rules, null);
    }

    private static bool IsRuleValid(RuleDto dto, out string error)
    {
        error = string.Empty;
        var knownTypes = new HashSet<string>(CustomRule.AvailableRuleTypes, StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(dto.RuleType) || !knownTypes.Contains(dto.RuleType))
        {
            error = $"规则类型无效或不支持：{dto.RuleType}";
            return false;
        }
        if (string.IsNullOrWhiteSpace(dto.Target))
        {
            error = "匹配内容不能为空";
            return false;
        }
        if (string.IsNullOrWhiteSpace(dto.Policy))
        {
            error = "策略不能为空";
            return false;
        }
        return true;
    }

    private static ConfigDto BuildConfigDto(string yaml, IClashSubService sub, AppSettings settings)
    {
        var stale = ComputeStale(sub, settings);
        return new ConfigDto
        {
            Yaml = yaml,
            LineCount = yaml.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length,
            RuleCount = CountRules(yaml),
            LastGoodUpdate = stale.LastGood,
            IsStale = stale.IsStale
        };
    }

    private static (DateTimeOffset? LastGood, bool IsStale) ComputeStale(IClashSubService sub, AppSettings settings)
    {
        // 统一口径：优先 LastUpstreamUpdate，其次 LastGoodUpdate（修复原三页面不一致）
        var reference = sub.GetLastUpstreamUpdate() ?? sub.GetLastGoodUpdate();
        var minutes = settings.CacheMinutes > 0 ? settings.CacheMinutes : 15;
        var isStale = !reference.HasValue || reference.Value < DateTimeOffset.Now.AddMinutes(-2 * minutes);
        return (reference, isStale);
    }

    private static int CountRules(string yaml)
    {
        int count = 0;
        bool inRules = false;
        int baseIndent = -1;
        foreach (var rawLine in yaml.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;
            var indent = line.Length - line.TrimStart(' ', '\t').Length;
            var trimmed = line.Trim();
            if (!inRules)
            {
                if (trimmed.StartsWith("rules:"))
                {
                    inRules = true;
                    baseIndent = indent;
                }
                continue;
            }
            if (trimmed.StartsWith('#')) continue;
            if (indent <= baseIndent) break;
            if (trimmed.StartsWith("- ")) count++;
        }
        return count;
    }

    private static async Task<(T? Value, bool Bad)> ReadJson<T>(Stream body) where T : class
    {
        var json = await new StreamReader(body).ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(json)) return (null, false);
        try
        {
            var value = JsonSerializer.Deserialize<T>(json, JsonOptions);
            return value == null ? (null, false) : (value, false);
        }
        catch (JsonException)
        {
            return (null, true);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static Task<AppSettings> storageGet(IServiceProvider sp)
        => sp.GetRequiredService<IStorageService>().GetSettingsAsync();
}

public class ParseRequest
{
    public string? Yaml { get; set; }
}

public class LoginRequest
{
    public string? Username { get; set; }
    public string? Password { get; set; }
}

public class ChangePasswordRequest
{
    public string? CurrentPassword { get; set; }
    public string? NewPassword { get; set; }
}

public class ImportRequest
{
    public List<CustomRule>? Rules { get; set; }
}

public class LatencyRequest
{
    public string? Name { get; set; }
}

public class BackupManifest
{
    public int FormatVersion { get; set; }
    public DateTimeOffset ExportedAt { get; set; }
    public List<string> Entries { get; set; } = new();
}

public class BackupPreviewResponse
{
    public DateTimeOffset ExportedAt { get; set; }
    public int FormatVersion { get; set; }
    public bool ContainsSettings { get; set; }
    public int RuleCount { get; set; }
    public List<string> Entries { get; set; } = new();
}

public class BackupImportResponse
{
    public DateTimeOffset ExportedAt { get; set; }
    public int FormatVersion { get; set; }
    public int RuleCount { get; set; }
    public List<string> RestoredEntries { get; set; } = new();
    public string BackupDir { get; set; } = string.Empty;
    public bool CredentialsChanged { get; set; }
}

public static class RuleDtoMapping
{
    public static CustomRule ToRule(this RuleDto dto)
        => new()
        {
            RuleType = dto.RuleType,
            Target = dto.Target,
            Policy = dto.Policy,
            Enabled = dto.Enabled,
            Remark = dto.Remark
        };
}