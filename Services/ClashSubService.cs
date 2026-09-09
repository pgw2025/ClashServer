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

    /// <summary>默认抓取超时（后台刷新等未显式指定方）</summary>
    public static readonly TimeSpan DefaultFetchTimeout = TimeSpan.FromSeconds(30);
    /// <summary>管理页面抓取超时：快速失败，配合 last-good 兜底</summary>
    public static readonly TimeSpan AdminFetchTimeout = TimeSpan.FromSeconds(5);
    /// <summary>/sub 公开端点抓取超时</summary>
    public static readonly TimeSpan PublicSubFetchTimeout = TimeSpan.FromSeconds(10);

    // last-good 快照：最后一次成功抓取的上游订阅（进程生命周期，ClearCache 不清除）
    private string? _lastGoodRawYaml;
    private string? _lastGoodUpstreamUrl;
    private DateTimeOffset? _lastGoodRawAt;

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

    public DateTimeOffset? GetLastGoodUpdate()
    {
        return _lastGoodRawAt;
    }

    public List<ProxyNode>? GetCachedNodes()
    {
        return _cache.TryGetValue(NodesCacheKey, out List<ProxyNode>? nodes) ? nodes : null;
    }

    public List<string>? GetCachedGroups()
    {
        return _cache.TryGetValue(GroupsCacheKey, out List<string>? groups) ? groups : null;
    }

    public async Task<string> GetMergedSubAsync(string baseUrl, bool forceRefresh = false, CancellationToken ct = default, TimeSpan? fetchTimeout = null)
    {
        var result = await GetMergedSubCoreAsync(baseUrl, forceRefresh, ct, fetchTimeout);
        return result.Yaml;
    }

    public async Task<SubFetchResult> GetMergedSubSafeAsync(string baseUrl, bool forceRefresh = false, CancellationToken ct = default, TimeSpan? fetchTimeout = null)
    {
        return await GetMergedSubCoreAsync(baseUrl, forceRefresh, ct, fetchTimeout);
    }

    private async Task<SubFetchResult> GetMergedSubCoreAsync(string baseUrl, bool forceRefresh, CancellationToken ct, TimeSpan? fetchTimeout)
    {
        if (!forceRefresh && _cache.TryGetValue(CacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        {
            DateTimeOffset? dataAt = _cache.TryGetValue(LastUpdateKey, out DateTimeOffset lastTs) ? lastTs : null;
            return new SubFetchResult(cached, false, dataAt);
        }

        var settings = await _storage.GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.UpstreamUrl))
        {
            throw new InvalidOperationException("未配置上游订阅 URL，请在设置中配置。");
        }

        try
        {
            // 抓取收敛：复用 Raw 抓取（锁、超时、快照、降级均在其中，全系统仅此一路真实抓取）
            var raw = await GetRawUpstreamCoreAsync(forceRefresh, ct, fetchTimeout);

            var rules = await _storage.GetRulesAsync();
            var enabledRules = rules.Where(r => r.Enabled).ToList();

            var mergedYaml = MergeCustomRules(raw.Yaml, enabledRules, settings.InsertRulesBefore, settings.ReplaceMode);

            if (settings.AutoGroupNodes)
            {
                mergedYaml = ApplyAutoGrouping(mergedYaml);
            }

            if (raw.Degraded)
            {
                // 降级数据不写入常规缓存，避免污染"最后更新时间"，保证上游恢复后普通请求自动探测
                _logger.LogWarning("merged 基于 last-good 快照生成（快照时间: {At}）", _lastGoodRawAt);
                return new SubFetchResult(mergedYaml, true, _lastGoodRawAt);
            }

            var cacheMinutes = settings.CacheMinutes > 0 ? settings.CacheMinutes : 15;
            var cacheOpts = new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(cacheMinutes),
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(Math.Min(cacheMinutes * 2, 1440))
            };
            _cache.Set(CacheKey, mergedYaml, cacheOpts);
            _cache.Set(LastUpdateKey, DateTimeOffset.Now, cacheOpts);

            return new SubFetchResult(mergedYaml, false, DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取或合并 Clash 订阅失败");
            if (!forceRefresh && _lastGoodRawYaml != null && string.Equals(_lastGoodUpstreamUrl, settings.UpstreamUrl, StringComparison.Ordinal))
            {
                _logger.LogWarning("上游不可达，降级从 last-good 快照重新合并（快照时间: {At}）", _lastGoodRawAt);
                var rules = await _storage.GetRulesAsync();
                var enabledRules = rules.Where(r => r.Enabled).ToList();

                var mergedYaml = MergeCustomRules(_lastGoodRawYaml, enabledRules, settings.InsertRulesBefore, settings.ReplaceMode);

                if (settings.AutoGroupNodes)
                {
                    mergedYaml = ApplyAutoGrouping(mergedYaml);
                }

                return new SubFetchResult(mergedYaml, true, _lastGoodRawAt);
            }
            throw;
        }
    }

    public async Task<bool> TestUpstreamAsync(CancellationToken ct = default)
    {
        try
        {
            var settings = await _storage.GetSettingsAsync();
            if (string.IsNullOrWhiteSpace(settings.UpstreamUrl)) return false;

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
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
                client.Timeout = TimeSpan.FromSeconds(5);
                using var resp = await client.GetAsync(settings.UpstreamUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }

    private static CancellationTokenSource CreateLinkedCts(CancellationToken ct, TimeSpan? fetchTimeout)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(fetchTimeout ?? DefaultFetchTimeout);
        return cts;
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

    /// <summary>
    /// 按节点名称中的国家/地区关键词自动分组，替换或插入 proxy-groups 段
    /// </summary>
    private static string ApplyAutoGrouping(string yaml)
    {
        // 国家/地区关键词映射：关键词 → 分组名
        var regionMap = new (string[] Keywords, string GroupName)[]
        {
            (new[] { "新加坡", "狮城", "sg", "singapore" }, "新加坡"),
            (new[] { "香港", "hk", "hongkong", "hong kong" }, "香港"),
            (new[] { "台湾", "tw", "taiwan", "tai wan" }, "台湾"),
            (new[] { "日本", "jp", "japan", "东京", "大阪" }, "日本"),
            (new[] { "美国", "us", "usa", "united states", "america", "洛杉矶", "圣何塞", "西雅图" }, "美国"),
            (new[] { "韩国", "kr", "korea", "首尔" }, "韩国"),
            (new[] { "英国", "uk", "england", "london", "伦敦" }, "英国"),
            (new[] { "德国", "de", "germany", "法兰克福" }, "德国"),
            (new[] { "法国", "fr", "france", "paris", "巴黎" }, "法国"),
            (new[] { "加拿大", "ca", "canada", "多伦多", "温哥华" }, "加拿大"),
            (new[] { "澳大利亚", "au", "australia", "悉尼" }, "澳大利亚"),
            (new[] { "俄罗斯", "ru", "russia", "莫斯科" }, "俄罗斯"),
            (new[] { "印度", "india", "孟买" }, "印度"),
            (new[] { "土耳其", "tr", "turkey", "伊斯坦布尔" }, "土耳其"),
            (new[] { "阿根廷", "ar", "argentina" }, "阿根廷"),
            (new[] { "巴西", "br", "brazil" }, "巴西"),
            (new[] { "荷兰", "nl", "netherlands", "amsterdam", "阿姆斯特丹" }, "荷兰"),
            (new[] { "菲律宾", "ph", "philippines" }, "菲律宾"),
            (new[] { "泰国", "th", "thailand" }, "泰国"),
            (new[] { "越南", "vn", "vietnam" }, "越南"),
        };

        var lines = yaml.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        // 1. 解析所有 proxy 名称
        var proxyNames = new List<string>();
        int proxiesLineIdx = -1;
        int proxiesIndent = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                continue;
            var trimmed = line.TrimStart();
            if (Regex.IsMatch(trimmed, @"^proxies\s*:"))
            {
                proxiesLineIdx = i;
                proxiesIndent = line.Length - trimmed.Length;
                break;
            }
        }

        if (proxiesLineIdx < 0) return yaml;

        // 读取 proxy 名称
        for (int i = proxiesLineIdx + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            var trimmed = line.TrimStart();
            var indent = line.Length - trimmed.Length;
            if (indent <= proxiesIndent && !string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith('#'))
                break;
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("-{"))
            {
                var rest = trimmed.Substring(2).Trim();
                if (rest.StartsWith("{"))
                {
                    // flow mapping
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
                                proxyNames.Add(val);
                                break;
                            }
                        }
                    }
                }
                else
                {
                    var m = Regex.Match(rest, @"^name\s*:\s*(.+?)(?:,\s|$)");
                    if (m.Success)
                    {
                        var name = m.Groups[1].Value.Trim().Trim('\'', '"');
                        if (!string.IsNullOrWhiteSpace(name))
                            proxyNames.Add(name);
                    }
                }
            }
        }

        if (proxyNames.Count == 0) return yaml;

        // 2. 按关键词分组
        var groups = new List<(string Name, List<string> Proxies)>();
        var assigned = new HashSet<string>();

        foreach (var (keywords, groupName) in regionMap)
        {
            var matched = new List<string>();
            foreach (var name in proxyNames)
            {
                if (assigned.Contains(name)) continue;
                var lower = name.ToLowerInvariant();
                foreach (var kw in keywords)
                {
                    if (lower.Contains(kw.ToLowerInvariant()))
                    {
                        matched.Add(name);
                        assigned.Add(name);
                        break;
                    }
                }
            }
            if (matched.Count > 0)
                groups.Add((groupName, matched));
        }

        // 未匹配的 → 其他
        var others = proxyNames.Where(n => !assigned.Contains(n)).ToList();
        if (others.Count > 0)
            groups.Add(("其他", others));

        // 3. 生成 proxy-groups YAML 文本
        var groupIndent = new string(' ', proxiesIndent);
        var itemIndent = new string(' ', proxiesIndent + 2);
        var fieldIndent = new string(' ', proxiesIndent + 4);

        var sb = new StringBuilder();
        sb.Append("proxy-groups:\n");

        // 全局选择组
        sb.Append($"{itemIndent}- name: \"节点选择\"\n");
        sb.Append($"{fieldIndent}type: select\n");
        sb.Append($"{fieldIndent}proxies:\n");
        foreach (var g in groups)
        {
            sb.Append($"{fieldIndent}  - \"{g.Name}\"\n");
        }
        sb.Append($"{fieldIndent}  - DIRECT\n");
        sb.Append($"{fieldIndent}  - REJECT\n");

        // 自动测速组
        sb.Append($"{itemIndent}- name: \"自动测速\"\n");
        sb.Append($"{fieldIndent}type: url-test\n");
        sb.Append($"{fieldIndent}url: http://www.gstatic.com/generate_204\n");
        sb.Append($"{fieldIndent}interval: 300\n");
        sb.Append($"{fieldIndent}proxies:\n");
        foreach (var name in proxyNames)
        {
            sb.Append($"{fieldIndent}  - \"{name}\"\n");
        }

        // 各地区分组
        foreach (var g in groups)
        {
            sb.Append($"{itemIndent}- name: \"{g.Name}\"\n");
            sb.Append($"{fieldIndent}type: url-test\n");
            sb.Append($"{fieldIndent}url: http://www.gstatic.com/generate_204\n");
            sb.Append($"{fieldIndent}interval: 300\n");
            sb.Append($"{fieldIndent}proxies:\n");
            foreach (var name in g.Proxies)
            {
                sb.Append($"{fieldIndent}  - \"{name}\"\n");
            }
        }

        // 故障转移组
        sb.Append($"{itemIndent}- name: \"故障转移\"\n");
        sb.Append($"{fieldIndent}type: fallback\n");
        sb.Append($"{fieldIndent}url: http://www.gstatic.com/generate_204\n");
        sb.Append($"{fieldIndent}interval: 300\n");
        sb.Append($"{fieldIndent}proxies:\n");
        foreach (var g in groups)
        {
            sb.Append($"{fieldIndent}  - \"{g.Name}\"\n");
        }

        var groupsYaml = sb.ToString();

        // 4. 替换或插入 proxy-groups 段
        // 查找已有的 proxy-groups 段
        int groupsLineIdx = -1;
        int groupsIndent = 0;
        int groupsEndIdx = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                continue;
            var trimmed = line.TrimStart();
            if (Regex.IsMatch(trimmed, @"^proxy-groups\s*:"))
            {
                groupsLineIdx = i;
                groupsIndent = line.Length - trimmed.Length;
                // 找到段结束位置
                for (int j = i + 1; j < lines.Length; j++)
                {
                    var l = lines[j];
                    if (string.IsNullOrWhiteSpace(l)) continue;
                    var t = l.TrimStart();
                    var ind = l.Length - t.Length;
                    if (ind <= groupsIndent && !string.IsNullOrWhiteSpace(t) && !t.StartsWith('#'))
                    {
                        groupsEndIdx = j;
                        break;
                    }
                }
                if (groupsEndIdx < 0) groupsEndIdx = lines.Length;
                break;
            }
        }

        var result = new List<string>();

        if (groupsLineIdx >= 0)
        {
            // 替换已有 proxy-groups 段
            for (int i = 0; i < groupsLineIdx; i++)
                result.Add(lines[i]);
            // 插入新的 proxy-groups
            foreach (var gl in groupsYaml.TrimEnd('\n').Split('\n'))
                result.Add(gl);
            // 插入剩余内容
            for (int i = groupsEndIdx; i < lines.Length; i++)
                result.Add(lines[i]);
        }
        else
        {
            // 没有 proxy-groups 段，在 proxies 段结束后插入
            // 找到 proxies 段结束
            int proxiesEndIdx = lines.Length;
            for (int i = proxiesLineIdx + 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                var trimmed = line.TrimStart();
                var indent = line.Length - trimmed.Length;
                if (indent <= proxiesIndent && !string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith('#'))
                {
                    proxiesEndIdx = i;
                    break;
                }
            }

            for (int i = 0; i < proxiesEndIdx; i++)
                result.Add(lines[i]);
            result.Add("");
            foreach (var gl in groupsYaml.TrimEnd('\n').Split('\n'))
                result.Add(gl);
            for (int i = proxiesEndIdx; i < lines.Length; i++)
                result.Add(lines[i]);
        }

        return string.Join("\n", result);
    }

    public async Task<List<ProxyNode>> GetProxyNodesAsync(string baseUrl, bool forceRefresh = false, CancellationToken ct = default, TimeSpan? fetchTimeout = null)
    {
        if (!forceRefresh && _cache.TryGetValue(NodesCacheKey, out List<ProxyNode>? cached) && cached != null)
        {
            return cached;
        }

        var rawYaml = await GetRawUpstreamYamlAsync(forceRefresh, ct, fetchTimeout);
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

    public async Task<string> GetRawUpstreamYamlAsync(bool forceRefresh = false, CancellationToken ct = default, TimeSpan? fetchTimeout = null)
    {
        var result = await GetRawUpstreamCoreAsync(forceRefresh, ct, fetchTimeout);
        return result.Yaml;
    }

    private async Task<RawFetchResult> GetRawUpstreamCoreAsync(bool forceRefresh, CancellationToken ct, TimeSpan? fetchTimeout)
    {
        if (!forceRefresh && _cache.TryGetValue(RawCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        {
            return new RawFetchResult(cached, false);
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

        try
        {
            // 全系统唯一抓取锁：保证同一时刻只有一路真实抓取
            var lockWait = TimeSpan.FromSeconds((fetchTimeout ?? DefaultFetchTimeout).TotalSeconds + 3);
            bool acquired = false;
            try
            {
                acquired = await _fetchLock.WaitAsync(lockWait, ct);
                if (!acquired)
                {
                    throw new TimeoutException($"等待订阅抓取锁超时（{lockWait.TotalSeconds:0}s），可能有其他请求正在进行抓取。");
                }

                // 双重检查：等待锁期间其他请求可能已成功抓取并写入缓存
                if (!forceRefresh && _cache.TryGetValue(RawCacheKey, out string? cachedInLock) && !string.IsNullOrEmpty(cachedInLock))
                {
                    return new RawFetchResult(cachedInLock, false);
                }

                using var cts = CreateLinkedCts(ct, fetchTimeout);
                using var resp = await client.GetAsync(settings.UpstreamUrl, HttpCompletionOption.ResponseContentRead, cts.Token);
                resp.EnsureSuccessStatusCode();

                var rawBytes = await resp.Content.ReadAsByteArrayAsync(cts.Token);
                var rawYaml = Encoding.UTF8.GetString(rawBytes).TrimStart('\uFEFF');

                var cacheMinutes = settings.CacheMinutes > 0 ? settings.CacheMinutes : 15;
                _cache.Set(RawCacheKey, rawYaml, TimeSpan.FromMinutes(cacheMinutes));

                // 更新 last-good 快照（记录所属上游 URL，URL 变更后自动失效）
                _lastGoodRawYaml = rawYaml;
                _lastGoodUpstreamUrl = settings.UpstreamUrl;
                _lastGoodRawAt = DateTimeOffset.Now;

                return new RawFetchResult(rawYaml, false);
            }
            finally
            {
                if (acquired) _fetchLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取上游订阅失败");
            if (!forceRefresh && _lastGoodRawYaml != null && string.Equals(_lastGoodUpstreamUrl, settings.UpstreamUrl, StringComparison.Ordinal))
            {
                _logger.LogWarning("上游不可达，降级返回 last-good 订阅数据（快照时间: {At}）", _lastGoodRawAt);
                return new RawFetchResult(_lastGoodRawYaml, true);
            }
            throw;
        }
    }

    public async Task<List<string>> GetProxyGroupsAsync(bool forceRefresh = false, CancellationToken ct = default, TimeSpan? fetchTimeout = null)
    {
        if (!forceRefresh && _cache.TryGetValue(GroupsCacheKey, out List<string>? cached) && cached != null)
            return cached;

        var settings = await _storage.GetSettingsAsync();

        List<string> groups;
        if (settings.AutoGroupNodes)
        {
            // 自动分组模式：返回自动生成的策略组名称
            var rawYaml = await GetRawUpstreamYamlAsync(forceRefresh, ct, fetchTimeout);
            var nodes = ParseProxyNodes(rawYaml);
            groups = GetAutoGeneratedGroupNames(nodes);
        }
        else
        {
            var rawYaml = await GetRawUpstreamYamlAsync(forceRefresh, ct, fetchTimeout);
            groups = ParseProxyGroups(rawYaml);
        }

        var cacheMinutes = settings.CacheMinutes > 0 ? settings.CacheMinutes : 15;
        _cache.Set(GroupsCacheKey, groups, TimeSpan.FromMinutes(cacheMinutes));
        return groups;
    }

    /// <summary>
    /// 根据代理节点列表计算自动生成的策略组名称
    /// </summary>
    private static List<string> GetAutoGeneratedGroupNames(List<ProxyNode> nodes)
    {
        var result = new List<string>
        {
            "节点选择",
            "自动测速",
            "故障转移"
        };

        var regionMap = new (string[] Keywords, string GroupName)[]
        {
            (new[] { "新加坡", "狮城", "sg", "singapore" }, "新加坡"),
            (new[] { "香港", "hk", "hongkong", "hong kong" }, "香港"),
            (new[] { "台湾", "tw", "taiwan", "tai wan" }, "台湾"),
            (new[] { "日本", "jp", "japan", "东京", "大阪" }, "日本"),
            (new[] { "美国", "us", "usa", "united states", "america", "洛杉矶", "圣何塞", "西雅图" }, "美国"),
            (new[] { "韩国", "kr", "korea", "首尔" }, "韩国"),
            (new[] { "英国", "uk", "england", "london", "伦敦" }, "英国"),
            (new[] { "德国", "de", "germany", "法兰克福" }, "德国"),
            (new[] { "法国", "fr", "france", "paris", "巴黎" }, "法国"),
            (new[] { "加拿大", "ca", "canada", "多伦多", "温哥华" }, "加拿大"),
            (new[] { "澳大利亚", "au", "australia", "悉尼" }, "澳大利亚"),
            (new[] { "俄罗斯", "ru", "russia", "莫斯科" }, "俄罗斯"),
            (new[] { "印度", "india", "孟买" }, "印度"),
            (new[] { "土耳其", "tr", "turkey", "伊斯坦布尔" }, "土耳其"),
            (new[] { "阿根廷", "ar", "argentina" }, "阿根廷"),
            (new[] { "巴西", "br", "brazil" }, "巴西"),
            (new[] { "荷兰", "nl", "netherlands", "amsterdam", "阿姆斯特丹" }, "荷兰"),
            (new[] { "菲律宾", "ph", "philippines" }, "菲律宾"),
            (new[] { "泰国", "th", "thailand" }, "泰国"),
            (new[] { "越南", "vn", "vietnam" }, "越南"),
        };

        var assigned = new HashSet<string>();
        foreach (var (keywords, groupName) in regionMap)
        {
            foreach (var node in nodes)
            {
                if (assigned.Contains(node.Name)) continue;
                var lower = node.Name.ToLowerInvariant();
                foreach (var kw in keywords)
                {
                    if (lower.Contains(kw.ToLowerInvariant()))
                    {
                        if (!result.Contains(groupName))
                            result.Add(groupName);
                        assigned.Add(node.Name);
                        break;
                    }
                }
            }
        }

        // 有未匹配节点时添加"其他"组
        if (nodes.Any(n => !assigned.Contains(n.Name)))
        {
            result.Add("其他");
        }

        return result;
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
