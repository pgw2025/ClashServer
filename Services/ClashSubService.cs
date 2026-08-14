using System.Text;
using System.Text.RegularExpressions;
using ClashServer.Models;
using Microsoft.Extensions.Caching.Memory;

namespace ClashServer.Services;

public class ClashSubService : IClashSubService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IStorageService _storage;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ClashSubService> _logger;
    private const string CacheKey = "clash_merged_sub_yaml";
    private static readonly SemaphoreSlim _fetchLock = new(1, 1);

    public ClashSubService(
        IHttpClientFactory httpClientFactory,
        IStorageService storage,
        IMemoryCache cache,
        ILogger<ClashSubService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _storage = storage;
        _cache = cache;
        _logger = logger;
    }

    public void ClearCache()
    {
        _cache.Remove(CacheKey);
    }

    public async Task<string> GetMergedSubAsync(string baseUrl, bool forceRefresh = false, CancellationToken ct = default)
    {
        if (!forceRefresh && _cache.TryGetValue(CacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        {
            return cached;
        }

        await _fetchLock.WaitAsync(ct);
        try
        {
            if (!forceRefresh && _cache.TryGetValue(CacheKey, out cached) && !string.IsNullOrEmpty(cached))
            {
                return cached;
            }

            var settings = await _storage.GetSettingsAsync();
            if (string.IsNullOrWhiteSpace(settings.UpstreamUrl))
            {
                throw new InvalidOperationException("未配置上游订阅 URL，请在设置中配置。");
            }

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ClashServer/1.0 (+https://github.com/clash)");

            using var resp = await client.GetAsync(settings.UpstreamUrl, HttpCompletionOption.ResponseContentRead, ct);
            resp.EnsureSuccessStatusCode();

            var rawBytes = await resp.Content.ReadAsByteArrayAsync(ct);
            var upstreamYaml = Encoding.UTF8.GetString(rawBytes);
            upstreamYaml = upstreamYaml.TrimStart('\uFEFF');

            var rules = await _storage.GetRulesAsync();
            var enabledRules = rules.Where(r => r.Enabled).OrderBy(r => r.UpdatedAt).ToList();

            var mergedYaml = MergeCustomRules(upstreamYaml, enabledRules, settings.InsertRulesBefore, settings.ReplaceMode);

            var cacheMinutes = settings.CacheMinutes > 0 ? settings.CacheMinutes : 15;
            var cacheOpts = new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(cacheMinutes),
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(Math.Min(cacheMinutes * 2, 1440))
            };
            _cache.Set(CacheKey, mergedYaml, cacheOpts);

            return mergedYaml;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取或合并 Clash 订阅失败");
            throw;
        }
        finally
        {
            _fetchLock.Release();
        }
    }

    public async Task<bool> TestUpstreamAsync(CancellationToken ct = default)
    {
        try
        {
            var settings = await _storage.GetSettingsAsync();
            if (string.IsNullOrWhiteSpace(settings.UpstreamUrl)) return false;

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            using var req = new HttpRequestMessage(HttpMethod.Head, settings.UpstreamUrl);
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            try
            {
                var settings = await _storage.GetSettingsAsync();
                if (string.IsNullOrWhiteSpace(settings.UpstreamUrl)) return false;
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                using var resp = await client.GetAsync(settings.UpstreamUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }

    public List<string> ParseYamlRules(string yamlRulesText)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(yamlRulesText)) return result;

        var lines = yamlRulesText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var inRulesBlock = false;
        var rulesIndent = -1;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line)) continue;
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith('#'))
            {
                if (inRulesBlock) continue;
                continue;
            }

            if (!inRulesBlock)
            {
                if (Regex.IsMatch(trimmed, @"^rules\s*:"))
                {
                    inRulesBlock = true;
                    rulesIndent = rawLine.Length - trimmed.Length;
                    continue;
                }
                continue;
            }

            var currentIndent = rawLine.Length - trimmed.Length;
            if (currentIndent <= rulesIndent && trimmed.Length > 0)
            {
                if (!trimmed.StartsWith("-"))
                {
                    inRulesBlock = false;
                    continue;
                }
            }

            if (trimmed.StartsWith("- "))
            {
                var ruleText = trimmed[2..].Trim();
                if (!string.IsNullOrWhiteSpace(ruleText))
                {
                    result.Add(ruleText);
                }
            }
            else if (trimmed.StartsWith('-'))
            {
                var ruleText = trimmed[1..].Trim();
                if (!string.IsNullOrWhiteSpace(ruleText))
                {
                    result.Add(ruleText);
                }
            }
        }

        if (result.Count == 0)
        {
            foreach (var rawLine in lines)
            {
                var trimmed = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#')) continue;
                if (Regex.IsMatch(trimmed, @"^rules\s*:")) continue;
                if (trimmed.StartsWith("- "))
                {
                    result.Add(trimmed[2..].Trim());
                }
                else if (trimmed.Contains(',') && trimmed.Length > 5)
                {
                    result.Add(trimmed);
                }
            }
        }

        return result;
    }

    private static string MergeCustomRules(string upstreamYaml, List<CustomRule> customRules, bool insertBefore, bool replaceMode)
    {
        // 移除 BOM
        upstreamYaml = upstreamYaml.TrimStart('\uFEFF');

        var lines = upstreamYaml.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        var customRuleLines = customRules
            .Select(r => r.ToYamlLine())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        // 1. 找到 "rules:" 行
        int rulesLineIdx = -1;
        int rulesIndent = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                continue;

            var trimmed = line.TrimStart();
            var indent = line.Length - trimmed.Length;

            if (Regex.IsMatch(trimmed, @"^rules\s*:"))
            {
                rulesLineIdx = i;
                rulesIndent = indent;
                break;
            }
        }

        // 没有 rules 段：在末尾添加
        if (rulesLineIdx < 0)
        {
            if (customRuleLines.Count == 0)
                return upstreamYaml;

            var defaultIndent = new string(' ', 2);
            var sb = new StringBuilder(upstreamYaml.TrimEnd());
            sb.Append('\n');
            sb.Append("rules:\n");
            foreach (var r in customRuleLines)
            {
                sb.Append($"{defaultIndent}- {r}\n");
            }
            return sb.ToString();
        }

        // 2. 找到 rules 段的规则行范围
        int contentStart = rulesLineIdx + 1;
        int contentEnd = contentStart;
        int itemIndent = -1;

        for (int i = contentStart; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                contentEnd = i + 1;
                continue;
            }

            var trimmed = line.TrimStart();
            var indent = line.Length - trimmed.Length;

            if (trimmed.StartsWith('#') && indent > rulesIndent)
            {
                contentEnd = i + 1;
                continue;
            }

            if (indent > rulesIndent && trimmed.StartsWith('-'))
            {
                if (itemIndent < 0) itemIndent = indent;
                contentEnd = i + 1;
            }
            else if (indent <= rulesIndent && !string.IsNullOrWhiteSpace(trimmed))
            {
                break;
            }
            else if (itemIndent >= 0)
            {
                contentEnd = i + 1;
            }
            else
            {
                break;
            }
        }

        // 3. 收集原有规则（除非是替换模式）
        var existingRules = new List<string>();
        if (!replaceMode)
        {
            for (int i = contentStart; i < contentEnd; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("- "))
                    existingRules.Add(trimmed.Substring(2).Trim());
                else if (trimmed.StartsWith('-'))
                {
                    var val = trimmed.Substring(1).Trim();
                    if (!string.IsNullOrWhiteSpace(val))
                        existingRules.Add(val);
                }
            }
        }

        // 4. 构建新的规则列表
        var indentStr = new string(' ', itemIndent > 0 ? itemIndent : rulesIndent + 2);

        List<string> finalRules;
        if (replaceMode)
        {
            finalRules = customRuleLines;
        }
        else if (insertBefore)
        {
            // 去重：如果自定义规则和上游规则完全相同，不重复添加上游的
            var customSet = new HashSet<string>(customRuleLines, StringComparer.Ordinal);
            finalRules = customRuleLines
                .Concat(existingRules.Where(r => !customSet.Contains(r)))
                .ToList();
        }
        else
        {
            var customSet = new HashSet<string>(customRuleLines, StringComparer.Ordinal);
            finalRules = existingRules
                .Concat(customRuleLines.Where(r => !existingRules.Contains(r, StringComparer.Ordinal)))
                .ToList();
        }

        // 5. 重建 YAML
        var result = new List<string>();

        for (int i = 0; i < rulesLineIdx; i++)
            result.Add(lines[i]);

        result.Add(lines[rulesLineIdx].Split('#')[0].TrimEnd());

        foreach (var r in finalRules)
            result.Add($"{indentStr}- {r}");

        for (int i = contentEnd; i < lines.Length; i++)
            result.Add(lines[i]);

        return string.Join("\n", result);
    }
}
