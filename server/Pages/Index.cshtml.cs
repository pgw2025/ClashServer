using ClashServer.Models;
using ClashServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ClashServer.Pages;

public class IndexModel : PageModel
{
    private readonly IStorageService _storage;
    private readonly IClashSubService _subService;

    public IndexModel(IStorageService storage, IClashSubService subService)
    {
        _storage = storage;
        _subService = subService;
    }

    public AppSettings Settings { get; set; } = new();
    public List<CustomRule> Rules { get; set; } = new();
    public List<ProxyNode> Nodes { get; set; } = new();
    public string SubUrl { get; set; } = string.Empty;
    public int EnabledRuleCount { get; set; }
    public bool? UpstreamOk { get; set; }
    public bool? RefreshOk { get; set; }
    public DateTimeOffset? LastUpstreamUpdate { get; set; }
    public DateTimeOffset? LastGoodUpdate { get; set; }
    public bool ShowStaleBanner { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        Settings = await _storage.GetSettingsAsync();
        Rules = await _storage.GetRulesAsync();
        EnabledRuleCount = Rules.Count(r => r.Enabled);
        BuildSubUrl();

        // 懒加载：服务端只读缓存，未命中时由前端 JS 异步拉取 /api/nodes，页面永不因抓取阻塞
        Nodes = _subService.GetCachedNodes() ?? new();
        LastUpstreamUpdate = _subService.GetLastUpstreamUpdate();
        LastGoodUpdate = _subService.GetLastGoodUpdate();

        ShowStaleBanner = IsDataStale();
    }

    public async Task<IActionResult> OnGetTestUpstreamAsync()
    {
        var ok = await _subService.TestUpstreamAsync(Request.HttpContext.RequestAborted);
        return new JsonResult(new { ok });
    }

    public async Task<IActionResult> OnGetTestLatencyAsync(string name)
    {
        try
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var nodes = await _subService.GetProxyNodesAsync(baseUrl, ct: Request.HttpContext.RequestAborted, fetchTimeout: AdminTimeout);
            var node = nodes.FirstOrDefault(n => n.Name == name);
            if (node == null)
                return new JsonResult(new { ok = false, error = "未找到该节点" });

            var result = await _subService.TestNodeLatencyAsync(node);
            return new JsonResult(new
            {
                ok = result.Error == null,
                name = result.Name,
                latency = result.Latency,
                error = result.Error
            });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { ok = false, error = ex.Message });
        }
    }

    public async Task<IActionResult> OnPostRefreshCacheAsync()
    {
        try
        {
            Settings = await _storage.GetSettingsAsync();
            Rules = await _storage.GetRulesAsync();
            EnabledRuleCount = Rules.Count(r => r.Enabled);
            BuildSubUrl();

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            await _subService.GetMergedSubAsync(baseUrl, forceRefresh: true, ct: Request.HttpContext.RequestAborted, fetchTimeout: PublicTimeout);
            Nodes = await _subService.GetProxyNodesAsync(baseUrl, forceRefresh: true, ct: Request.HttpContext.RequestAborted, fetchTimeout: PublicTimeout);
            RefreshOk = true;
            StatusMessage = "✅ 缓存已刷新，最新的订阅内容已重新拉取并合并。";
        }
        catch (Exception ex)
        {
            RefreshOk = false;
            StatusMessage = "❌ 刷新失败: " + ex.Message;
        }
        return RedirectToPage();
    }

    private void BuildSubUrl()
    {
        var req = Request;
        var url = $"{req.Scheme}://{req.Host}/sub";
        if (!string.IsNullOrWhiteSpace(Settings.AccessToken))
        {
            url += $"?token={Uri.EscapeDataString(Settings.AccessToken)}";
        }
        SubUrl = url;
    }

    private TimeSpan AdminTimeout => TimeSpan.FromSeconds(Settings.AdminFetchTimeoutSeconds > 0 ? Settings.AdminFetchTimeoutSeconds : 5);

    private TimeSpan PublicTimeout => TimeSpan.FromSeconds(Settings.PublicSubFetchTimeoutSeconds > 0 ? Settings.PublicSubFetchTimeoutSeconds : 10);

    private bool IsDataStale()
    {
        var lastOk = LastUpstreamUpdate ?? LastGoodUpdate;
        var staleMinutes = Settings.CacheMinutes > 0 ? Settings.CacheMinutes : 15;
        return !lastOk.HasValue || lastOk.Value < DateTimeOffset.Now.AddMinutes(-2 * staleMinutes);
    }
}
