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
        // 等待应用启动完成
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = await _storage.GetSettingsAsync();
                if (!string.IsNullOrWhiteSpace(settings.UpstreamUrl))
                {
                    // 强制刷新订阅缓存 + 节点缓存
                    await _subService.GetMergedSubAsync("", forceRefresh: true, stoppingToken);
                    await _subService.GetProxyNodesAsync("", forceRefresh: true, stoppingToken);
                    _logger.LogInformation("后台定时刷新上游订阅成功");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "后台定时刷新上游订阅失败");
            }

            // 按配置的缓存时长等待下一次刷新
            var cacheMinutes = 15;
            try
            {
                var settings = await _storage.GetSettingsAsync();
                cacheMinutes = settings.CacheMinutes > 0 ? settings.CacheMinutes : 15;
            }
            catch { }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(cacheMinutes), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
