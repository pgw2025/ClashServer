using ClashServer.Models;

namespace ClashServer.Services;

public interface IStorageService
{
    Task<List<CustomRule>> GetRulesAsync();
    Task SaveRulesAsync(List<CustomRule> rules);
    Task<AppSettings> GetSettingsAsync();
    Task SaveSettingsAsync(AppSettings settings);
}
