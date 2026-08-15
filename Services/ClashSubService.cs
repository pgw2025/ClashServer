using System.Net.NetworkInformation;
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
    private const string RawCacheKey = "clash_raw_upstream_yaml";
    private const string NodesCacheKey = "clash_proxy_nodes";
    private const string GroupsCacheKey = "clash_proxy_groups";
    private const string LastUpdateKey = "clash_last_upstream_update";
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
        _cache.Remove(RawCacheKey);
        _cache.Remove(NodesCacheKey);
        _cache.Remove(GroupsCacheKey);
        _cache.Remove(LastUpdateKey);
    }

    public DateTimeOffset? GetLastUpstreamUpdate()
    {
        return _cache.TryGetValue(LastUpdateKey, out DateTimeOffset ts) ? ts : null;
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
            client.DefaultRequestHeaders.UserAgent.ParseAdd("clash-verge/v2.0.3");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Connection", "keep-alive");

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
            _cache.Set(LastUpdateKey, DateTimeOffset.Now, cacheOpts);

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

    public async Task<List<ProxyNode>> GetProxyNodesAsync(string baseUrl, bool forceRefresh = false, CancellationToken ct = default)
    {
        if (!forceRefresh && _cache.TryGetValue(NodesCacheKey, out List<ProxyNode>? cached) && cached != null)
        {
            return cached;
        }

        var rawYaml = await GetRawUpstreamYamlAsync(forceRefresh, ct);
        var nodes = ParseProxyNodes(rawYaml);

        var settings = await _storage.GetSettingsAsync();
        var cacheMinutes = settings.CacheMinutes > 0 ? settings.CacheMinutes : 15;
        _cache.Set(NodesCacheKey, nodes, TimeSpan.FromMinutes(cacheMinutes));

        return nodes;
    }

    public async Task<ProxyNode> TestNodeLatencyAsync(ProxyNode node, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(node.Server))
        {
            node.Error = "节点缺少 server 信息";
            return node;
        }

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(node.Server, TimeSpan.FromSeconds(5), null, null, ct);

            if (reply.Status == IPStatus.Success)
            {
                node.Latency = (int)reply.RoundtripTime;
                node.Error = null;
            }
            else
            {
                node.Latency = null;
                node.Error = $"Ping 失败: {reply.Status}";
            }
        }
        catch (OperationCanceledException)
        {
            node.Latency = null;
            node.Error = "超时 (>5s)";
        }
        catch (Exception ex)
        {
            node.Latency = null;
            node.Error = ex.Message;
        }

        return node;
    }

    public async Task<string> GetRawUpstreamYamlAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        if (!forceRefresh && _cache.TryGetValue(RawCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
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
        client.DefaultRequestHeaders.UserAgent.ParseAdd("clash-verge/v2.0.3");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Connection", "keep-alive");

        using var resp = await client.GetAsync(settings.UpstreamUrl, HttpCompletionOption.ResponseContentRead, ct);
        resp.EnsureSuccessStatusCode();

        var rawBytes = await resp.Content.ReadAsByteArrayAsync(ct);
        var rawYaml = Encoding.UTF8.GetString(rawBytes).TrimStart('\uFEFF');

        var cacheMinutes = settings.CacheMinutes > 0 ? settings.CacheMinutes : 15;
        _cache.Set(RawCacheKey, rawYaml, TimeSpan.FromMinutes(cacheMinutes));

        return rawYaml;
    }

    public async Task<List<string>> GetProxyGroupsAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        if (!forceRefresh && _cache.TryGetValue(GroupsCacheKey, out List<string>? cached) && cached != null)
            return cached;

        var rawYaml = await GetRawUpstreamYamlAsync(forceRefresh, ct);
        var groups = ParseProxyGroups(rawYaml);

        var settings = await _storage.GetSettingsAsync();
        var cacheMinutes = settings.CacheMinutes > 0 ? settings.CacheMinutes : 15;
        _cache.Set(GroupsCacheKey, groups, TimeSpan.FromMinutes(cacheMinutes));
        return groups;
    }

    private static List<string> ParseProxyGroups(string yaml)
    {
        var groups = new List<string>();
        if (string.IsNullOrWhiteSpace(yaml)) return groups;

        var lines = yaml.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        bool inSection = false;
        int baseIndent = -1;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var trimmed = line.Trim();
            var indent = line.Length - line.TrimStart(' ', '\t').Length;

            if (trimmed.StartsWith('#')) continue;

            if (!inSection)
            {
                if (trimmed.StartsWith("proxy-groups:"))
                {
                    inSection = true;
                    baseIndent = indent;
                }
                continue;
            }

            // 同级别或更外层，退出 section
            if (indent <= baseIndent && !trimmed.StartsWith("- ")) break;

            if (trimmed.StartsWith("- "))
            {
                var rest = trimmed.Substring(2).Trim();
                // flow mapping: - { name: '香港01', type: select, ... }
                if (rest.StartsWith("{"))
                {
                    var fields = SplitFlowFields(rest.TrimStart('{').TrimEnd('}'));
                    foreach (var field in fields)
                    {
                        var colon = field.IndexOf(':');
                        if (colon > 0)
                        {
                            var key = field.Substring(0, colon).Trim();
                            var val = field.Substring(colon + 1).Trim().Trim('\'', '"');
                            if (key == "name" && !string.IsNullOrWhiteSpace(val))
                            {
                                if (!groups.Contains(val)) groups.Add(val);
                                break;
                            }
                        }
                    }
                }
                else
                {
                    // block 风格: - name: 香港01
                    var flowMatch = System.Text.RegularExpressions.Regex.Match(rest, @"^name\s*:\s*([^\s,]+)");
                    if (flowMatch.Success)
                    {
                        var name = flowMatch.Groups[1].Value.Trim('\'', '"');
                        if (!string.IsNullOrWhiteSpace(name) && !groups.Contains(name))
                            groups.Add(name);
                    }
                }
            }
            else if (trimmed.StartsWith("name:") && groups.Count > 0)
            {
                // 多行 block 里的 name: xxx 已在上面处理 (rest 里是 `- name: xxx`)
            }
        }
        return groups;
    }

    private static List<ProxyNode> ParseProxyNodes(string yaml)
    {
        var nodes = new List<ProxyNode>();
        if (string.IsNullOrWhiteSpace(yaml)) return nodes;

        var lines = yaml.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        int proxiesLineIdx = -1;
        int proxiesIndent = 0;

        // 找到 proxies: 行
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                continue;

            var trimmed = line.TrimStart();
            var indent = line.Length - trimmed.Length;

            if (Regex.IsMatch(trimmed, @"^proxies\s*:"))
            {
                proxiesLineIdx = i;
                proxiesIndent = indent;
                break;
            }
        }

        if (proxiesLineIdx < 0) return nodes;

        // 解析每个 proxy 条目
        ProxyNode? current = null;
        int itemIndent = -1;

        for (int i = proxiesLineIdx + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            var trimmed = line.TrimStart();
            var indent = line.Length - trimmed.Length;

            // 遇到同级或更高级的键，proxies 段结束
            if (indent <= proxiesIndent && !trimmed.StartsWith('#'))
            {
                if (current != null) nodes.Add(current);
                break;
            }

            // 新的 proxy 条目（以 - 开头）
            if (trimmed.StartsWith('-'))
            {
                if (current != null) nodes.Add(current);
                current = new ProxyNode();
                itemIndent = indent;

                var afterDash = trimmed.Substring(1).Trim();

                // Flow mapping 风格: { name: xxx, type: ss, ... }
                if (afterDash.StartsWith('{'))
                {
                    ParseFlowMapping(current, afterDash);
                }
                // Block 风格: name: xxx （行内首个键值对）
                else if (afterDash.Length > 0 && afterDash.Contains(':'))
                {
                    ApplyProxyField(current, afterDash);
                }
            }
            else if (current != null && indent > itemIndent)
            {
                // Block 风格: 后续缩进行
                if (trimmed.Contains(':'))
                {
                    ApplyProxyField(current, trimmed);
                }
            }
        }

        if (current != null) nodes.Add(current);

        return nodes;
    }

    /// <summary>
    /// 解析 flow mapping 风格的 proxy 条目，如 { name: xxx, type: ss, server: host, port: 443 }
    /// </summary>
    private static void ParseFlowMapping(ProxyNode node, string content)
    {
        // 去掉外层大括号
        content = content.Trim();
        if (content.StartsWith('{')) content = content.Substring(1);
        var lastBrace = content.LastIndexOf('}');
        if (lastBrace >= 0) content = content.Substring(0, lastBrace);

        // 按逗号拆分（尊重嵌套大括号和引号）
        var parts = SplitFlowFields(content);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0 && trimmed.Contains(':'))
            {
                ApplyProxyField(node, trimmed);
            }
        }
    }

    /// <summary>
    /// 按逗号拆分 flow mapping 内容，跳过嵌套大括号和引号内的逗号
    /// </summary>
    private static List<string> SplitFlowFields(string content)
    {
        var parts = new List<string>();
        var depth = 0;
        var inQuote = false;
        char quoteChar = '\0';
        int start = 0;

        for (int i = 0; i < content.Length; i++)
        {
            var c = content[i];

            if (inQuote)
            {
                if (c == quoteChar) inQuote = false;
            }
            else if (c == '\'' || c == '"')
            {
                inQuote = true;
                quoteChar = c;
            }
            else if (c == '{' || c == '[')
            {
                depth++;
            }
            else if (c == '}' || c == ']')
            {
                depth--;
            }
            else if (c == ',' && depth <= 0)
            {
                parts.Add(content.Substring(start, i - start));
                start = i + 1;
            }
        }

        if (start < content.Length)
        {
            parts.Add(content.Substring(start));
        }

        return parts;
    }

    private static void ApplyProxyField(ProxyNode node, string text)
    {
        var colonIdx = text.IndexOf(':');
        if (colonIdx < 0) return;

        var key = text.Substring(0, colonIdx).Trim();
        var value = text.Substring(colonIdx + 1).Trim().Trim('\'', '"');

        switch (key)
        {
            case "name":
                node.Name = value;
                break;
            case "type":
                node.Type = value;
                break;
            case "server":
                node.Server = value;
                break;
            case "port":
                if (int.TryParse(value, out var port))
                    node.Port = port;
                break;
        }
    }
}
