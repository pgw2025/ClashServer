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
    public int RawLineCount { get; set; }
    public int MergedLineCount { get; set; }
    public int RawRuleCount { get; set; }
    public int MergedRuleCount { get; set; }
    public int CustomRuleCount { get; set; }

    public async Task OnGetAsync()
    {
        try
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            RawUpstreamYaml = await _subService.GetRawUpstreamYamlAsync();
            MergedYaml = await _subService.GetMergedSubAsync(baseUrl);
            LastUpdate = _subService.GetLastUpstreamUpdate();

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
    }

    public async Task<IActionResult> OnPostRefreshAsync()
    {
        try
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            await _subService.GetRawUpstreamYamlAsync(forceRefresh: true);
            await _subService.GetMergedSubAsync(baseUrl, forceRefresh: true);
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
}
