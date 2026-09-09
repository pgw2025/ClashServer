using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ClashServer.Models;

public class AppSettings
{
    [JsonPropertyName("upstreamUrl")]
    [Display(Name = "上游订阅 URL")]
    [Url(ErrorMessage = "请输入有效的 URL")]
    public string? UpstreamUrl { get; set; }

    [JsonPropertyName("accessToken")]
    [Display(Name = "访问 Token")]
    [StringLength(128, MinimumLength = 4, ErrorMessage = "Token 长度应为 4-128 个字符")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("cacheMinutes")]
    [Display(Name = "缓存时长 (分钟)")]
    [Range(1, 1440, ErrorMessage = "缓存时长应为 1-1440 分钟")]
    public int CacheMinutes { get; set; } = 15;

    [JsonPropertyName("adminFetchTimeoutSeconds")]
    [Display(Name = "管理页面抓取超时 (秒)")]
    [Range(1, 60, ErrorMessage = "超时时间应为 1-60 秒")]
    public int AdminFetchTimeoutSeconds { get; set; } = 5;

    [JsonPropertyName("publicSubFetchTimeoutSeconds")]
    [Display(Name = "订阅端点抓取超时 (秒)")]
    [Range(1, 60, ErrorMessage = "超时时间应为 1-60 秒")]
    public int PublicSubFetchTimeoutSeconds { get; set; } = 10;

    [JsonPropertyName("insertRulesBefore")]
    [Display(Name = "自定义规则插入位置")]
    public bool InsertRulesBefore { get; set; } = true;

    [JsonPropertyName("replaceMode")]
    [Display(Name = "完全替换模式")]
    public bool ReplaceMode { get; set; } = false;

    [JsonPropertyName("autoGroupNodes")]
    [Display(Name = "自动分组节点")]
    public bool AutoGroupNodes { get; set; } = true;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
