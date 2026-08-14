using ClashServer.Models;
using ClashServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ClashServer.Pages;

public class SettingsModel : PageModel
{
    private readonly IStorageService _storage;
    private readonly IClashSubService _subService;

    public SettingsModel(IStorageService storage, IClashSubService subService)
    {
        _storage = storage;
        _subService = subService;
    }

    [BindProperty]
    public AppSettings Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        Input = await _storage.GetSettingsAsync();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        await _storage.SaveSettingsAsync(Input);
        _subService.ClearCache();
        StatusMessage = "✅ 设置已保存，订阅缓存已刷新";
        return RedirectToPage();
    }

    public IActionResult OnPostGenerateToken()
    {
        var token = Guid.NewGuid().ToString("N")[..16];
        StatusMessage = $"🔑 已生成新 Token: <code class=\"fw-semibold\">{token}</code> 请记得点击保存按钮确认应用。";
        TempData["SuggestedToken"] = token;
        Input.AccessToken = token;
        return RedirectToPage();
    }
}
