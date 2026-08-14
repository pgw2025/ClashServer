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
    public string? NodesError { get; set; }
    public DateTimeOffset? LastUpstreamUpdate { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        Settings = await _storage.GetSettingsAsync();
        Rules = await _storage.GetRulesAsync();
        EnabledRuleCount = Rules.Count(r => r.Enabled);
        BuildSubUrl();

        try
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            Nodes = await _subService.GetProxyNodesAsync(baseUrl);
            LastUpstreamUpdate = _subService.GetLastUpstreamUpdate();
        }
        catch (Exception ex)
        {
            NodesError = ex.Message;
        }
    }

    public async Task<IActionResult> OnGetTestUpstreamAsync()
    {
        var ok = await _subService.TestUpstreamAsync();
        return new JsonResult(new { ok });
    }

    public async Task<IActionResult> OnGetTestLatencyAsync(string name)
    {
        try
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var nodes = await _subService.GetProxyNodesAsync(baseUrl);
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
            await _subService.GetMergedSubAsync(baseUrl, forceRefresh: true);
            Nodes = await _subService.GetProxyNodesAsync(baseUrl, forceRefresh: true);
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
}
