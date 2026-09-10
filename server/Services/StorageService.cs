using System.Text.Json;
using ClashServer.Models;
using ClashServer.Web;

namespace ClashServer.Services;

public class StorageService : IStorageService
{
    private readonly string _dataDir;
    private readonly string _rulesPath;
    private readonly string _settingsPath;
    private static readonly SemaphoreSlim _rulesLock = new(1, 1);
    private static readonly SemaphoreSlim _settingsLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public StorageService(IWebHostEnvironment env)
    {
        _dataDir = Path.Combine(env.ContentRootPath, "Data");
        _rulesPath = Path.Combine(_dataDir, "rules.json");
        _settingsPath = Path.Combine(_dataDir, "settings.json");

        if (!Directory.Exists(_dataDir))
        {
            Directory.CreateDirectory(_dataDir);
        }
    }

    public async Task<List<CustomRule>> GetRulesAsync()
    {
        await _rulesLock.WaitAsync();
        try
        {
            if (!File.Exists(_rulesPath))
            {
                return new List<CustomRule>();
            }

            using var stream = new FileStream(_rulesPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            var result = await JsonSerializer.DeserializeAsync<List<CustomRule>>(stream, JsonOptions);
            return result ?? new List<CustomRule>();
        }
        finally
        {
            _rulesLock.Release();
        }
    }

    public async Task SaveRulesAsync(List<CustomRule> rules)
    {
        await _rulesLock.WaitAsync();
        try
        {
            using var stream = new FileStream(_rulesPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
            await JsonSerializer.SerializeAsync(stream, rules, JsonOptions);
            await stream.FlushAsync();
        }
        finally
        {
            _rulesLock.Release();
        }
    }

    public async Task<AppSettings> GetSettingsAsync()
    {
        await _settingsLock.WaitAsync();
        try
        {
            AppSettings settings;
            if (!File.Exists(_settingsPath))
            {
                settings = new AppSettings
                {
                    AccessToken = Guid.NewGuid().ToString("N")[..16]
                };
            }
            else
            {
                using var stream = new FileStream(_settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
                var result = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions);
                settings = result ?? new AppSettings
                {
                    AccessToken = Guid.NewGuid().ToString("N")[..16]
                };
            }

            // 引导：passwordHash 为空且存在明文 password 时，首次读取自动哈希化并清除明文（幂等）。
            // 锁已持有，直接落盘，避免 SaveSettingsAsync 重入死锁。
            if (string.IsNullOrWhiteSpace(settings.PasswordHash)
                && !string.IsNullOrWhiteSpace(settings.Username)
                && !string.IsNullOrWhiteSpace(settings.Password))
            {
                settings.PasswordHash = AuthSetup.HashPassword(settings.Password);
                settings.Password = null;
                settings.UpdatedAt = DateTime.Now;
                using var write = new FileStream(_settingsPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
                await JsonSerializer.SerializeAsync(write, settings, JsonOptions);
                await write.FlushAsync();
            }

            return settings;
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        settings.UpdatedAt = DateTime.Now;
        await _settingsLock.WaitAsync();
        try
        {
            using var stream = new FileStream(_settingsPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions);
            await stream.FlushAsync();
        }
        finally
        {
            _settingsLock.Release();
        }
    }
}
