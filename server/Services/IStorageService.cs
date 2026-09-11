using ClashServer.Models;

namespace ClashServer.Services;

public interface IStorageService
{
    Task<List<CustomRule>> GetRulesAsync();
    Task SaveRulesAsync(List<CustomRule> rules);
    Task<AppSettings> GetSettingsAsync();
    Task SaveSettingsAsync(AppSettings settings);

    // 数据备份/还原：读取原始落盘字节，避免经 DTO 往返造成无损不一致
    Task<byte[]> GetRawSettingsFileAsync();
    Task<byte[]> GetRawRulesFileAsync();
    /// <summary>把当前 settings/rules 快照到 Data/backup/&lt;时间戳&gt;/，返回相对目录名。</summary>
    Task<string> SnapshotBackupAsync();
    Task WriteRawSettingsFileAsync(byte[] content);
    Task WriteRawRulesFileAsync(byte[] content);
}
