using System.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace ClashServer.Services;

public class BackgroundRefreshService : BackgroundService
{
    private readonly IClashSubService _subService;
    private readonly IStorageService _storage;
    private readonly ILogger<BackgroundRefreshService> _logger;

    public BackgroundRefreshService(
        IClashSubService subService,
        IStorageService storage,
        ILogger<BackgroundRefreshService> logger)
    {
        _subService = subService;
        _storage = storage;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("后台刷新服务已启动，3秒后开始首次刷新");
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var startTime = DateTimeOffset.Now;
            var sw = Stopwatch.StartNew();
            bool hasUpstream = false;
            bool subOk = false;
            bool nodesOk = false;
            int ruleCount = 0;
            int nodeCount = 0;
            Exception? error = null;

            try
            {
                var settings = await _storage.GetSettingsAsync();
                hasUpstream = !string.IsNullOrWhiteSpace(settings.UpstreamUrl);

                if (!hasUpstream)
                {
                    _logger.LogWarning("【刷新跳过】开始时间: {Start} | 原因: 未配置上游订阅 URL，下次将在 {Interval} 分钟后重试",
                        startTime.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        settings.CacheMinutes > 0 ? settings.CacheMinutes : 15);
                }
                else
                {
                    _logger.LogInformation("【刷新开始】开始时间: {Start} | 上游: {Url}",
                        startTime.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        MaskUrl(settings.UpstreamUrl));

                    sw.Restart();
                    var mergedYaml = await _subService.GetMergedSubAsync("", forceRefresh: true, stoppingToken);
                    var subMs = sw.ElapsedMilliseconds;
                    subOk = true;
                    ruleCount = CountRulesInYaml(mergedYaml);
                    _logger.LogInformation("  ↳ 订阅合并完成 | 耗时: {Ms}ms | 规则总数: {Count}", subMs, ruleCount);

                    sw.Restart();
                    var nodes = await _subService.GetProxyNodesAsync("", forceRefresh: true, stoppingToken);
                    var nodesMs = sw.ElapsedMilliseconds;
                    nodesOk = true;
                    nodeCount = nodes.Count;
                    _logger.LogInformation("  ↳ 节点解析完成 | 耗时: {Ms}ms | 节点数: {Count}", nodesMs, nodeCount);
                }
            }
            catch (Exception ex)
            {
                error = ex;
                _logger.LogError(ex, "【刷新异常】开始时间: {Start} | 阶段: {Stage} | 订阅: {SubOk} | 节点: {NodesOk}",
                    startTime.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    subOk ? "节点阶段" : "订阅阶段",
                    subOk, nodesOk);
            }
            finally
            {
                sw.Stop();
            }

            if (hasUpstream)
            {
                var totalMs = sw.ElapsedMilliseconds;
                var status = (subOk && nodesOk) ? "成功" : "失败";
                _logger.LogInformation("【刷新结束】状态: {Status} | 总耗时: {TotalMs}ms | 订阅:{SubOk} 节点:{NodesOk} | 规则:{RuleCount} 节点:{NodeCount} | 下次刷新: {Interval} 分钟后",
                    status,
                    totalMs,
                    subOk ? "✅" : "❌",
                    nodesOk ? "✅" : "❌",
                    ruleCount,
                    nodeCount,
                    await GetCacheMinutesOrDefaultAsync());
            }

            var cacheMinutes = await GetCacheMinutesOrDefaultAsync();
            try
            {
                _logger.LogInformation("等待 {Min} 分钟后执行下一次刷新...", cacheMinutes);
                await Task.Delay(TimeSpan.FromMinutes(cacheMinutes), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("后台刷新服务已停止");
                break;
            }
        }
    }

    private static string MaskUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        try
        {
            var uri = new Uri(url);
            return $"{uri.Scheme}://{uri.Host}:{uri.Port}/***";
        }
        catch
        {
            if (url.Length > 30) return url.Substring(0, 30) + "...";
            return url;
        }
    }

    private static int CountRulesInYaml(string yaml)
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

    private async Task<int> GetCacheMinutesOrDefaultAsync()
    {
        try
        {
            var settings = await _storage.GetSettingsAsync();
            return settings.CacheMinutes > 0 ? settings.CacheMinutes : 15;
        }
        catch
        {
            return 15;
        }
    }
}
