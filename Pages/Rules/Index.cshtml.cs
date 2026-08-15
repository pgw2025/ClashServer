using ClashServer.Models;
using ClashServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ClashServer.Pages.Rules;

public class IndexModel : PageModel
{
    private readonly IStorageService _storage;
    private readonly IClashSubService _subService;

    public IndexModel(IStorageService storage, IClashSubService subService)
    {
        _storage = storage;
        _subService = subService;
    }

    public List<CustomRule> Rules { get; set; } = new();

    [BindProperty]
    public CustomRule InputRule { get; set; } = new();

    public List<string> ProxyGroups { get; set; } = new();

    public string? GroupsError { get; set; }

    public string? EditingId { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(string? edit = null)
    {
        Rules = (await _storage.GetRulesAsync())
            .OrderByDescending(r => r.UpdatedAt)
            .ToList();

        try
        {
            ProxyGroups = await _subService.GetProxyGroupsAsync();
        }
        catch (Exception ex)
        {
            GroupsError = ex.Message;
        }

        EditingId = edit;
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id)
    {
        var rules = await _storage.GetRulesAsync();
        var rule = rules.FirstOrDefault(r => r.Id == id);
        if (rule != null)
        {
            rule.Enabled = !rule.Enabled;
            rule.UpdatedAt = DateTime.Now;
            await _storage.SaveRulesAsync(rules);
            _subService.ClearCache();
            StatusMessage = rule.Enabled ? "✅ 规则已启用" : "✅ 规则已禁用";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var rules = await _storage.GetRulesAsync();
        var before = rules.Count;
        rules.RemoveAll(r => r.Id == id);
        if (rules.Count < before)
        {
            await _storage.SaveRulesAsync(rules);
            _subService.ClearCache();
            StatusMessage = "🗑️ 规则已删除";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpsertAsync()
    {
        if (!ModelState.IsValid)
        {
            Rules = (await _storage.GetRulesAsync())
                .OrderByDescending(r => r.UpdatedAt).ToList();
            try { ProxyGroups = await _subService.GetProxyGroupsAsync(); } catch { }
            StatusMessage = "❌ 输入有误，请检查字段";
            return Page();
        }

        var rules = await _storage.GetRulesAsync();

        if (InputRule.Id == Guid.Empty)
        {
            InputRule.Id = Guid.NewGuid();
            InputRule.UpdatedAt = DateTime.Now;
            rules.Add(InputRule);
            StatusMessage = "✅ 已新增规则";
        }
        else
        {
            var idx = rules.FindIndex(r => r.Id == InputRule.Id);
            if (idx < 0)
            {
                InputRule.Id = Guid.NewGuid();
                InputRule.UpdatedAt = DateTime.Now;
                rules.Add(InputRule);
                StatusMessage = "✅ 已新增规则";
            }
            else
            {
                InputRule.UpdatedAt = DateTime.Now;
                rules[idx] = InputRule;
                StatusMessage = "✅ 已更新规则";
            }
        }

        await _storage.SaveRulesAsync(rules);
        _subService.ClearCache();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostClearAllAsync()
    {
        await _storage.SaveRulesAsync(new List<CustomRule>());
        _subService.ClearCache();
        StatusMessage = "🧹 所有规则已清除";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostBatchPolicyAsync(string[] selectedIds, string batchPolicy)
    {
        if (selectedIds == null || selectedIds.Length == 0 || string.IsNullOrWhiteSpace(batchPolicy))
        {
            StatusMessage = "⚠ 未选择规则或策略为空";
            return RedirectToPage();
        }

        var ids = selectedIds.Select(Guid.Parse).ToHashSet();
        var rules = await _storage.GetRulesAsync();
        int count = 0;
        foreach (var rule in rules)
        {
            if (ids.Contains(rule.Id))
            {
                rule.Policy = batchPolicy;
                rule.UpdatedAt = DateTime.Now;
                count++;
            }
        }
        await _storage.SaveRulesAsync(rules);
        _subService.ClearCache();
        StatusMessage = $"✅ 已批量修改 {count} 条规则的策略为「{batchPolicy}」";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnGetJsonAsync(Guid id)
    {
        var rules = await _storage.GetRulesAsync();
        var rule = rules.FirstOrDefault(r => r.Id == id);
        if (rule == null) return NotFound();
        return new JsonResult(rule);
    }
}
