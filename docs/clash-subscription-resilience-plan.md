# Clash 订阅服务韧性（优雅降级）改造方案 v2（修订版）

> 状态：待评审 / 未执行（仅方案，未改代码）
> 目标：订阅不可达时，页面 **毫秒级用上次成功缓存兜底渲染**，不再整站卡死 30~90s；浏览器断开即取消；并正确告知用户“当前为缓存兜底数据”。

---

## 0. 与原方案（v1）相比的关键修正

v1 评审发现的硬伤已在本方案中修正：

| 级别 | v1 问题 | 本方案修正 |
|---|---|---|
| P0 | `GetMergedSubAsync` 的兜底 `return fallback`（来自 `CacheKey`）在 `catch` 里永远走不到（进入 catch 前 `CacheKey` 必已失效） | 改为 `GetMergedSubAsync` **复用** `GetRawUpstreamYamlAsync`（单一兜底路径），并独立维护 `LastGoodMergedKey` 兜底键 |
| P0 | 锁超时 `WaitAsync` 后 `finally` 无条件 `Release()` 会破坏 `SemaphoreSlim` 计数 | 引入 `acquired` 标记，仅在真正获得锁时 `Release()` |
| P1 | 超时 30s 行号写错（写成 `L110/L123/L136`，实际 30s 仅 `L70` 与 `L711`，`L123/L135` 已是 10s 且属 `TestUpstreamAsync`） | 仅 `L70`、`L711` 两处 30s；统一降到 **15s** |
| P1 | 各方法 `ct` 参数位置写错（`GetMergedSubAsync` 第 3 参、`GetProxyGroupsAsync` 第 2 参、`GetProxyNodesAsync` 第 3 参） | 已按真实签名校正 |
| P2 | 验证清单要求“降级提示”，但 v1 没加任何提示代码 → 用户静默拿旧数据 | 新增 `IsDataStale()` / `GetLastUpstreamUpdate()` + 页面横幅，明确标识“缓存兜底” |
| P2 | 自愈完全依赖后台刷新 | 兜底数据**不写入常规缓存键**，下游恢复后经后台 `forceRefresh` 或点击刷新自动愈合 |
| P2 | 强制刷新失败仍返回 `ok:true` | 刷新处理返回 `stale` 标志与降级文案 |
| P2 | `GetMergedSubAsync` 重复抓取 upstream | 复用 `GetRawUpstreamYamlAsync`，删除一份独立 `HttpClient` 抓取逻辑 |

---

## 1. 改动汇总表

| # | 文件 | 位置（当前行号） | 改动 | 解决痛点 |
|---|---|---|---|---|
| 1 | `Services/ClashSubService.cs` | 顶部常量 L15-19 | 新增 `LastGoodRawKey` / `LastGoodMergedKey` 两个 24h 兜底键；新增 `LockTimeoutMs` | 兜底数据生命周期 |
| 2 | `Services/ClashSubService.cs` | `ClearCache` L34-41 | 增加移除 `LastGoodRawKey` / `LastGoodMergedKey` | 配置变更/清空时同步失效兜底 |
| 3 | `Services/ClashSubService.cs` | `GetRawUpstreamYamlAsync` L697-727 | 返回值改为 `(string Yaml, bool Stale)` 元组；加 `LastGoodRawKey` 兜底与 15s 超时；仅成功抓取时写 `LastUpdateKey` | 核心兜底 + 取消令牌生效 |
| 4 | `Services/ClashSubService.cs` | `GetMergedSubAsync` L48-113 | 复用 `GetRawUpstreamYamlAsync`；`WaitAsync(LockTimeoutMs, ct)` + `acquired` 保护 `Release()`；仅新鲜结果写 `CacheKey`/`LastUpdateKey`，兜底结果仅写 `LastGoodMergedKey` | 修 P0-1/P0-2，避免重复抓取 |
| 5 | `Services/ClashSubService.cs` | `GetProxyNodesAsync` L642-657、`GetProxyGroupsAsync` L729-753 | 适配元组返回值（`var (raw, _) = ...`） | 依赖 raw 兜底，无需独立改动 |
| 6 | `Services/ClashSubService.cs` | 新增 | `IsDataStale()` 辅助方法 | 页面横幅判定 |
| 7 | `Pages/Config.cshtml.cs` | `OnGetAsync` L29-50、`OnPostRefreshAsync` L52-65 | 调用传 `ct: RequestAborted`；`OnGet` 计算 `StaleWarning` | 断开即取消 + 降级提示 |
| 8 | `Pages/Index.cshtml.cs` | L42、L62、L93 | 调用传 `ct: RequestAborted`；用 `LastUpstreamUpdate` 显示降级横幅 | 同上 |
| 9 | `Pages/Rules/Index.cshtml.cs` | L44 | `GetProxyGroupsAsync(ct: RequestAborted)`；可选降级横幅 | 同上 |
| 10 | `Services/BackgroundRefreshService.cs` | L97-102（可选） | 失败时拉长下次等待间隔 | 避免频繁打挂掉的上游 |

---

## 2. 逐项详细改动（Before / After）

### 改动 1 & 2 — 常量与 `ClearCache`

**Before (L15-19, L34-41):**
```csharp
private const string CacheKey = "clash_merged_sub_yaml";
private const string RawCacheKey = "clash_raw_upstream_yaml";
private const string NodesCacheKey = "clash_proxy_nodes";
private const string GroupsCacheKey = "clash_proxy_groups";
private const string LastUpdateKey = "clash_last_upstream_update";
private static readonly SemaphoreSlim _fetchLock = new(1, 1);

public void ClearCache()
{
    _cache.Remove(CacheKey);
    _cache.Remove(RawCacheKey);
    _cache.Remove(NodesCacheKey);
    _cache.Remove(GroupsCacheKey);
    _cache.Remove(LastUpdateKey);
}
```

**After:**
```csharp
private const string CacheKey = "clash_merged_sub_yaml";
private const string RawCacheKey = "clash_raw_upstream_yaml";
private const string NodesCacheKey = "clash_proxy_nodes";
private const string GroupsCacheKey = "clash_proxy_groups";
private const string LastUpdateKey = "clash_last_upstream_update";
// 新增：24h 兜底键（仅在成功抓取时写入，过期后保留旧值用于降级）
private const string LastGoodRawKey = "clash_last_good_raw_yaml";
private const string LastGoodMergedKey = "clash_last_good_merged_sub";
// 新增：抢锁超时（ms），避免页面/后台互相阻塞放大卡顿
private const int LockTimeoutMs = 5000;
private static readonly SemaphoreSlim _fetchLock = new(1, 1);

public void ClearCache()
{
    _cache.Remove(CacheKey);
    _cache.Remove(RawCacheKey);
    _cache.Remove(NodesCacheKey);
    _cache.Remove(GroupsCacheKey);
    _cache.Remove(LastUpdateKey);
    _cache.Remove(LastGoodRawKey);
    _cache.Remove(LastGoodMergedKey);
}
```

---

### 改动 3 — `GetRawUpstreamYamlAsync`（核心兜底，返回元组）

**Before (L697-727):**
```csharp
public async Task<string> GetRawUpstreamYamlAsync(bool forceRefresh = false, CancellationToken ct = default)
{
    if (!forceRefresh && _cache.TryGetValue(RawCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        return cached;

    var settings = await _storage.GetSettingsAsync();
    if (string.IsNullOrWhiteSpace(settings.UpstreamUrl))
        throw new InvalidOperationException("未配置上游订阅 URL，请在设置中配置。");

    var client = _httpClientFactory.CreateClient();
    client.Timeout = TimeSpan.FromSeconds(30);
    // ... headers ...
    using var resp = await client.GetAsync(settings.UpstreamUrl, HttpCompletionOption.ResponseContentRead, ct);
    resp.EnsureSuccessStatusCode();
    var rawBytes = await resp.Content.ReadAsByteArrayAsync(ct);
    var rawYaml = Encoding.UTF8.GetString(rawBytes).TrimStart('\uFEFF');

    var cacheMinutes = settings.CacheMinutes > 0 ? settings.CacheMinutes : 15;
    _cache.Set(RawCacheKey, rawYaml, TimeSpan.FromMinutes(cacheMinutes));
    return rawYaml;
}
```

**After:**
```csharp
/// <summary>
/// 返回 (Yaml, Stale)。Stale=true 表示本次返回的是 24h 兜底旧数据而非实时抓取。
/// </summary>
public async Task<(string Yaml, bool Stale)> GetRawUpstreamYamlAsync(bool forceRefresh = false, CancellationToken ct = default)
{
    // 1) 常规缓存命中（新鲜数据）→ 直接返回
    if (!forceRefresh && _cache.TryGetValue(RawCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        return (cached, false);

    // 2) 常规缓存过期但存在 24h 兜底 → 非强制刷新时直接返回旧数据，不阻塞、不联网
    if (!forceRefresh && _cache.TryGetValue(LastGoodRawKey, out string? lastGood) && !string.IsNullOrEmpty(lastGood))
        return (lastGood, true);

    try
    {
        var settings = await _storage.GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.UpstreamUrl))
            throw new InvalidOperationException("未配置上游订阅 URL，请在设置中配置。");

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);   // 30s → 15s
        client.DefaultRequestHeaders.UserAgent.ParseAdd("clash-verge/v2.0.3");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Connection", "keep-alive");

        using var resp = await client.GetAsync(settings.UpstreamUrl, HttpCompletionOption.ResponseContentRead, ct);
        resp.EnsureSuccessStatusCode();

        var rawBytes = await resp.Content.ReadAsByteArrayAsync(ct);
        var rawYaml = Encoding.UTF8.GetString(rawBytes).TrimStart('\uFEFF');

        var cacheMinutes = settings.CacheMinutes > 0 ? settings.CacheMinutes : 15;
        // 仅成功抓取时写常规缓存与“最后成功时间”
        _cache.Set(RawCacheKey, rawYaml, TimeSpan.FromMinutes(cacheMinutes));
        _cache.Set(LastGoodRawKey, rawYaml, TimeSpan.FromHours(24));     // 兜底长缓存
        _cache.Set(LastUpdateKey, DateTimeOffset.Now, TimeSpan.FromHours(24));
        return (rawYaml, false);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "上游 YAML 抓取失败，回落使用上次成功缓存");
        if (_cache.TryGetValue(LastGoodRawKey, out string? fallback) && !string.IsNullOrEmpty(fallback))
            return (fallback, true);   // 关键：回落旧配置，不再抛异常
        throw;                          // 实在没有任何兜底数据才抛
    }
}
```

---

### 改动 4 — `GetMergedSubAsync`（复用 raw + 安全锁 + 新鲜度感知）

**Before (L48-113):** 自行 `HttpClient.GetAsync` 抓取上游（与 raw 重复），`catch` 直接 `throw`，`finally` 无条件 `Release()`。

**After:**
```csharp
public async Task<string> GetMergedSubAsync(string baseUrl, bool forceRefresh = false, CancellationToken ct = default)
{
    if (!forceRefresh && _cache.TryGetValue(CacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        return cached;

    bool acquired = false;
    try
    {
        // 锁等待加超时，避免页面与后台互相阻塞放大卡顿
        if (!await _fetchLock.WaitAsync(LockTimeoutMs, ct))
            throw new TimeoutException("等待订阅锁超时，请稍后重试");
        acquired = true;

        if (!forceRefresh && _cache.TryGetValue(CacheKey, out cached) && !string.IsNullOrEmpty(cached))
            return cached;

        var settings = await _storage.GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.UpstreamUrl))
        {
            // 未配置 URL 也尽量回落旧合并结果
            if (_cache.TryGetValue(LastGoodMergedKey, out string? lastGoodMerged) && !string.IsNullOrEmpty(lastGoodMerged))
                return lastGoodMerged;
            throw new InvalidOperationException("未配置上游订阅 URL，请在设置中配置。");
        }

        // 复用 raw 抓取（其内部已带 lastGood 兜底），避免重复请求同一 upstream
        var (upstreamYaml, rawStale) = await GetRawUpstreamYamlAsync(forceRefresh, ct);

        var rules = await _storage.GetRulesAsync();
        var enabledRules = rules.Where(r => r.Enabled).ToList();

        var mergedYaml = MergeCustomRules(upstreamYaml, enabledRules, settings.InsertRulesBefore, settings.ReplaceMode);
        if (settings.AutoGroupNodes)
            mergedYaml = ApplyAutoGrouping(mergedYaml);

        var cacheMinutes = settings.CacheMinutes > 0 ? settings.CacheMinutes : 15;
        // 兜底（stale）结果只写 LastGoodMergedKey，不写常规 CacheKey / LastUpdateKey
        // → 下游恢复后后台 forceRefresh 或用户刷新可自动愈合，且页面横幅能正确识别“缓存兜底”
        _cache.Set(LastGoodMergedKey, mergedYaml, TimeSpan.FromHours(24));
        if (!rawStale)
        {
            _cache.Set(CacheKey, mergedYaml, TimeSpan.FromMinutes(cacheMinutes));
            _cache.Set(LastUpdateKey, DateTimeOffset.Now, TimeSpan.FromHours(24));
        }
        return mergedYaml;
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "获取或合并 Clash 订阅失败，回落使用上次成功缓存");
        if (_cache.TryGetValue(LastGoodMergedKey, out string? fallback) && !string.IsNullOrEmpty(fallback))
            return fallback;                 // 关键：回落旧合并结果
        throw;                               // 真的没有任何数据才抛
    }
    finally
    {
        if (acquired) _fetchLock.Release(); // 仅在真正获得锁时释放，防止信号量计数被破坏
    }
}
```

---

### 改动 5 — `GetProxyNodesAsync` / `GetProxyGroupsAsync` 适配元组

**Before (L649 / L740 / L746):** `var rawYaml = await GetRawUpstreamYamlAsync(forceRefresh, ct);`
**After:** `var (rawYaml, _) = await GetRawUpstreamYamlAsync(forceRefresh, ct);`

> `GetProxyGroupsAsync` 内有两处调用（L740、L746），均改为解构即可。无需独立兜底逻辑——raw 已兜底。

---

### 改动 6 — 新增 `IsDataStale()` 辅助方法

放在 `GetLastUpstreamUpdate()`（L43-46）附近：

```csharp
public DateTimeOffset? GetLastUpstreamUpdate()
{
    return _cache.TryGetValue(LastUpdateKey, out DateTimeOffset ts) ? ts : null;
}

/// <summary>
/// 当前对外提供的数据是否为“缓存兜底”（非实时抓取）。用于页面降级横幅。
/// </summary>
public bool IsDataStale()
{
    if (!_cache.TryGetValue(LastUpdateKey, out DateTimeOffset ts))
        return true;
    // 超过约 1.5 倍常规缓存窗口即认为已过期（兜底）
    return (DateTimeOffset.Now - ts) > TimeSpan.FromMinutes(30);
}
```

> 阈值用固定 30min 近似（常规 `CacheMinutes` 默认 15）。如需严格对齐，可让页面传入 `settings.CacheMinutes` 自行比较 `LastUpstreamUpdate`。

---

### 改动 7 — `Pages/Config.cshtml.cs`

**OnGetAsync（L29-50）Before:**
```csharp
RawUpstreamYaml = await _subService.GetRawUpstreamYamlAsync();
MergedYaml = await _subService.GetMergedSubAsync(baseUrl);
LastUpdate = _subService.GetLastUpstreamUpdate();
```
**After:**
```csharp
RawUpstreamYaml = await _subService.GetRawUpstreamYamlAsync(ct: RequestAborted);
MergedYaml = await _subService.GetMergedSubAsync(baseUrl, ct: RequestAborted);
LastUpdate = _subService.GetLastUpstreamUpdate();
// 新增：降级横幅判定
StaleWarning = _subService.IsDataStale()
    ? $"⚠ 当前显示的是缓存兜底数据（最后成功更新：{LastUpdate:yyyy-MM-dd HH:mm}），上游订阅暂不可达。"
    : null;
```
并在 Model 增加属性 `public string? StaleWarning { get; set; }`，在 `Config.cshtml` 顶部渲染该提示。

**OnPostRefreshAsync（L52-65）Before:**
```csharp
await _subService.GetRawUpstreamYamlAsync(forceRefresh: true);
await _subService.GetMergedSubAsync(baseUrl, forceRefresh: true);
...
return new JsonResult(new { ok = true });
```
**After（修 P2-5，失败/降级都明确告知）:**
```csharp
await _subService.GetRawUpstreamYamlAsync(forceRefresh: true, ct: RequestAborted);
await _subService.GetMergedSubAsync(baseUrl, forceRefresh: true, ct: RequestAborted);
var stale = _subService.IsDataStale();
return new JsonResult(new
{
    ok = !stale,
    stale,
    message = stale ? "上游暂不可达，已返回缓存兜底数据" : "订阅已刷新"
});
```

---

### 改动 8 — `Pages/Index.cshtml.cs`

- L42：`Nodes = await _subService.GetProxyNodesAsync(baseUrl, ct: RequestAborted);`
- L62：`var nodes = await _subService.GetProxyNodesAsync(baseUrl, ct: RequestAborted);`
- L93：`await _subService.GetProxyNodesAsync(baseUrl, forceRefresh: true, ct: RequestAborted);`
- 利用已有的 `LastUpstreamUpdate` 在 `Index.cshtml` 渲染降级横幅（同 Config 的 `StaleWarning` 逻辑）。

> `OnGetTestUpstreamAsync`（L53）与 `TestUpstreamAsync` 已自带 10s 超时、且 `TestUpstreamAsync(CancellationToken ct = default)` 存在——可顺手在 L53 改为 `await _subService.TestUpstreamAsync(RequestAborted);`（可选，非必须）。

---

### 改动 9 — `Pages/Rules/Index.cshtml.cs`

- L44：`ProxyGroups = await _subService.GetProxyGroupsAsync(ct: RequestAborted);`
- 可选：在 `OnGetAsync` 中同样用 `_subService.IsDataStale()` 设置降级提示（与 Config/Index 一致）。

---

### 改动 10 — `Services/BackgroundRefreshService.cs`（可选）

L97-102 当前失败也会按 `GetCacheMinutesOrDefaultAsync()` 等待。为避免对“已挂掉的上游”频繁重试，可在 `catch`（L70-77）分支里用更长间隔：

```csharp
// ExecuteAsync 的 catch 分支内，记录失败后改用更长退避
var waitMinutes = error != null ? Math.Max(await GetCacheMinutesOrDefaultAsync(), 30) : await GetCacheMinutesOrDefaultAsync();
await Task.Delay(TimeSpan.FromMinutes(waitMinutes), stoppingToken);
```
> 注意：此改动需把 L97-102 的 `cacheMinutes` 用 `waitMinutes` 替代；属于锦上添花，可后续单独评估。

---

## 3. 验证清单（修订版，与改动一致）

1. **正常订阅**：各页面秒开，节点/规则正常，`/sub` 正常出 YAML，`LastUpstreamUpdate` 为最近时间，`StaleWarning` 为 null。
2. **上游不可达**（改 URL 为不可达地址）：刷新配置对比页 / 仪表盘 / 规则管理页应在 **1 秒内**出页，显示**旧数据 + 顶部“缓存兜底”横幅**，而非空白或 Loading 30s。
3. **浏览器打开页面后立即关闭/刷新**：服务端通过 `RequestAborted` 快速取消 `GetAsync` 与 `WaitAsync`，日志中不应出现整段 15~30s 等待。
4. **连点多次“刷新缓存”**：锁等待 5s 超时快速回落（抛 `TimeoutException` 并由页面捕获提示），不会串行堆积。
5. **上游恢复后**：后台 `forceRefresh`（每 `cacheMinutes` 分钟）或用户点击刷新 → 重新抓取成功 → `LastUpdateKey` 更新 → 横幅消失，数据自动愈合。
6. **清空缓存（`ClearCache`，如规则增删改触发）**：兜底键一并清除；下次请求重新抓取或重新建立兜底。

---

## 4. 风险与决策点（供审批）

- **D1（阈值）**：`IsDataStale()` 用固定 30min 近似，还是严格读取 `settings.CacheMinutes` 由页面比较？前者简单，后者精确。建议先用固定阈值。
- **D2（超时 15s）**：15s 比原 30s 更不易误伤慢但存活的上游，但若订阅很大且网络慢仍可能误兜底。如担心，可改 `ResponseHeadersRead` + 限时读取，或保持 20s。
- **D3（双键 vs 单键）**：本方案用 `CacheKey`（新鲜）+ `LastGoodMergedKey`（兜底）双键，以保证“自愈 + 正确横幅”。若希望实现更简单，可只用 `LastGoodMergedKey` 并始终返回它（牺牲自愈速度），不推荐。
- **D4（改动 10 后台退避）**：非必须，建议作为独立小改动后续评估，不纳入本轮。
- **并发说明**：`_servingStale` 未用共享字段（改为元组 `Stale` 随调用返回），不存在跨请求竞态；`LastUpdateKey` 为时间戳，页面读取安全。
