using ClashServer.Models;
using ClashServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ClashServer.Pages;

public class ConfigModel : PageModel
{
    private readonly IClashSubService _subService;
    private readonly IStorageService _storage;

    public ConfigModel(IClashSubService subService, IStorageService storage)
    {
        _subService = subService;
        _storage = storage;
    }

    public string RawUpstreamYaml { get; set; } = string.Empty;
    public string MergedYaml { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? LastUpdate { get; set; }
    public DateTimeOffset? LastGoodUpdate { get; set; }
    public bool ShowStaleBanner { get; set; }
    public int RawLineCount { get; set; }
    public int MergedLineCount { get; set; }
    public int RawRuleCount { get; set; }
    public int MergedRuleCount { get; set; }
    public int CustomRuleCount { get; set; }

    private int _cacheMinutes = 15;
    private int _adminTimeoutSeconds = 5;
    private int _publicTimeoutSeconds = 10;

    public async Task OnGetAsync()
    {
        try
        {
            var settings = await _storage.GetSettingsAsync();
            _cacheMinutes = settings.CacheMinutes > 0 ? settings.CacheMinutes : 15;
            _adminTimeoutSeconds = settings.AdminFetchTimeoutSeconds > 0 ? settings.AdminFetchTimeoutSeconds : 5;
            _publicTimeoutSeconds = settings.PublicSubFetchTimeoutSeconds > 0 ? settings.PublicSubFetchTimeoutSeconds : 10;

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            RawUpstreamYaml = await _subService.GetRawUpstreamYamlAsync(ct: Request.HttpContext.RequestAborted, fetchTimeout: TimeSpan.FromSeconds(_adminTimeoutSeconds));
            MergedYaml = await _subService.GetMergedSubAsync(baseUrl, ct: Request.HttpContext.RequestAborted, fetchTimeout: TimeSpan.FromSeconds(_adminTimeoutSeconds));
            LastUpdate = _subService.GetLastUpstreamUpdate();
            LastGoodUpdate = _subService.GetLastGoodUpdate();

            RawLineCount = RawUpstreamYaml.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            MergedLineCount = MergedYaml.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            RawRuleCount = CountRules(RawUpstreamYaml);
            MergedRuleCount = CountRules(MergedYaml);

            var rules = await _storage.GetRulesAsync();
            CustomRuleCount = rules.Count(r => r.Enabled);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        ShowStaleBanner = IsDataStale();
    }

    public async Task<IActionResult> OnPostRefreshAsync()
    {
        try
        {
            var settings = await _storage.GetSettingsAsync();
            _publicTimeoutSeconds = settings.PublicSubFetchTimeoutSeconds > 0 ? settings.PublicSubFetchTimeoutSeconds : 10;

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            await _subService.GetRawUpstreamYamlAsync(forceRefresh: true, ct: Request.HttpContext.RequestAborted, fetchTimeout: TimeSpan.FromSeconds(_publicTimeoutSeconds));
            await _subService.GetMergedSubAsync(baseUrl, forceRefresh: true, ct: Request.HttpContext.RequestAborted, fetchTimeout: TimeSpan.FromSeconds(_publicTimeoutSeconds));
        }
        catch (Exception ex)
        {
            return new JsonResult(new { ok = false, error = ex.Message });
        }
        return new JsonResult(new { ok = true });
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

    private bool IsDataStale()
    {
        var lastOk = LastUpdate ?? LastGoodUpdate;
        return !lastOk.HasValue || lastOk.Value < DateTimeOffset.Now.AddMinutes(-2 * _cacheMinutes);
    }
}
