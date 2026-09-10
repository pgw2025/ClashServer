using ClashServer.Models;
using ClashServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ClashServer.Pages.Rules;

public class ImportModel : PageModel
{
    private readonly IStorageService _storage;
    private readonly IClashSubService _subService;

    public ImportModel(IStorageService storage, IClashSubService subService)
    {
        _storage = storage;
        _subService = subService;
    }

    [BindProperty]
    public string? YamlText { get; set; }

    public List<CustomRule>? PreviewRules { get; set; }

    public int ImportedCount { get; set; }

    public bool ShowPreview { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public void OnGet()
    {
    }

    public IActionResult OnPostPreview()
    {
        if (string.IsNullOrWhiteSpace(YamlText))
        {
            StatusMessage = "请粘贴或上传 YAML 规则文本";
            return Page();
        }

        var lines = _subService.ParseYamlRules(YamlText);
        PreviewRules = new List<CustomRule>();
        foreach (var line in lines)
        {
            var rule = ParseLineToRule(line);
            if (rule != null) PreviewRules.Add(rule);
        }
        ShowPreview = true;
        return Page();
    }

    public async Task<IActionResult> OnPostImportAsync()
    {
        if (string.IsNullOrWhiteSpace(YamlText))
        {
            StatusMessage = "请粘贴或上传 YAML 规则文本";
            return Page();
        }

        var lines = _subService.ParseYamlRules(YamlText);
        var existing = await _storage.GetRulesAsync();
        var added = 0;
        var newRules = new List<CustomRule>();
        foreach (var line in lines)
        {
            var rule = ParseLineToRule(line);
            if (rule == null) continue;
            rule.Id = Guid.NewGuid();
            rule.UpdatedAt = DateTime.Now;
            newRules.Add(rule);
            added++;
        }
        newRules.Reverse();
        foreach (var rule in newRules)
        {
            existing.Insert(0, rule);
        }
        await _storage.SaveRulesAsync(existing);
        _subService.ClearCache();

        StatusMessage = $"✅ 成功导入 {added} 条规则";
        return RedirectToPage("/Rules/Index");
    }

    public async Task<IActionResult> OnPostUploadAsync(IFormFile? yamlFile)
    {
        if (yamlFile == null || yamlFile.Length == 0)
        {
            StatusMessage = "请选择要上传的 .yaml/.yml/.txt 文件";
            return Page();
        }

        using var reader = new StreamReader(yamlFile.OpenReadStream());
        YamlText = await reader.ReadToEndAsync();

        var lines = _subService.ParseYamlRules(YamlText);
        PreviewRules = new List<CustomRule>();
        foreach (var line in lines)
        {
            var rule = ParseLineToRule(line);
            if (rule != null) PreviewRules.Add(rule);
        }
        ShowPreview = true;
        ImportedCount = PreviewRules.Count;
        StatusMessage = $"已解析文件 {yamlFile.FileName}，发现 {PreviewRules.Count} 条规则，请确认后点击确认导入。";
        return Page();
    }

    private static CustomRule? ParseLineToRule(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        line = line.Trim();
        if (line.StartsWith('#')) return null;

        var hashIdx = line.IndexOf('#');
        string? remark = null;
        var mainPart = line;
        if (hashIdx >= 0)
        {
            remark = line.Substring(hashIdx + 1).Trim();
            mainPart = line.Substring(0, hashIdx).Trim();
        }

        var parts = mainPart.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return null;

        var rule = new CustomRule
        {
            RuleType = parts[0],
            Target = parts[1],
            Policy = parts.Length >= 3 ? parts[2] : "PROXY",
            Enabled = true,
            Remark = remark
        };

        var validTypes = new HashSet<string>(CustomRule.AvailableRuleTypes, StringComparer.OrdinalIgnoreCase);
        var validPolicies = new HashSet<string>(CustomRule.AvailablePolicies, StringComparer.OrdinalIgnoreCase)
        {
            "Proxy", "代理", "Direct", "Reject", "No-Resolve"
        };

        if (!validTypes.Contains(rule.RuleType))
        {
            return null;
        }

        if (!validPolicies.Contains(rule.Policy) && !rule.Policy.Contains('组', StringComparison.Ordinal)
                                                  && rule.Policy.Length < 80)
        {
        }

        return rule;
    }
}
