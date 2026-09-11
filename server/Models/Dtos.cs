using System.Text.Json.Serialization;

namespace ClashServer.Models.Dtos;

/// <summary>统一的 API 响应结构，承载数据与 stale 元数据。</summary>
public class ApiResponse<T>
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("data")]
    public T? Data { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("lastGoodUpdate")]
    public DateTimeOffset? LastGoodUpdate { get; set; }

    [JsonPropertyName("isStale")]
    public bool IsStale { get; set; }

    public static ApiResponse<T> Success(T? data, DateTimeOffset? lastGood = null, bool isStale = false)
        => new() { Ok = true, Data = data, LastGoodUpdate = lastGood, IsStale = isStale };

    public static ApiResponse<T> Failure(string error)
        => new() { Ok = false, Error = error };
}

/// <summary>规则对外 DTO（时间字段用 DateTimeOffset，避免无时区歧义）。</summary>
public class RuleDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("ruleType")]
    public string RuleType { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("policy")]
    public string Policy { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("remark")]
    public string? Remark { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    public static RuleDto From(CustomRule rule)
        => new()
        {
            Id = rule.Id,
            RuleType = rule.RuleType,
            Target = rule.Target,
            Policy = rule.Policy,
            Enabled = rule.Enabled,
            Remark = rule.Remark,
            UpdatedAt = new DateTimeOffset(rule.UpdatedAt)
        };
}

/// <summary>设置对外 DTO。</summary>
public class SettingsDto
{
    [JsonPropertyName("upstreamUrl")]
    public string? UpstreamUrl { get; set; }

    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("cacheMinutes")]
    public int CacheMinutes { get; set; }

    [JsonPropertyName("adminFetchTimeoutSeconds")]
    public int AdminFetchTimeoutSeconds { get; set; }

    [JsonPropertyName("publicSubFetchTimeoutSeconds")]
    public int PublicSubFetchTimeoutSeconds { get; set; }

    [JsonPropertyName("insertRulesBefore")]
    public bool InsertRulesBefore { get; set; }

    [JsonPropertyName("replaceMode")]
    public bool ReplaceMode { get; set; }

    [JsonPropertyName("autoGroupNodes")]
    public bool AutoGroupNodes { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    public static SettingsDto From(AppSettings s)
        => new()
        {
            UpstreamUrl = s.UpstreamUrl,
            AccessToken = s.AccessToken,
            Username = s.Username,
            CacheMinutes = s.CacheMinutes,
            AdminFetchTimeoutSeconds = s.AdminFetchTimeoutSeconds,
            PublicSubFetchTimeoutSeconds = s.PublicSubFetchTimeoutSeconds,
            InsertRulesBefore = s.InsertRulesBefore,
            ReplaceMode = s.ReplaceMode,
            AutoGroupNodes = s.AutoGroupNodes,
            UpdatedAt = new DateTimeOffset(s.UpdatedAt)
        };
}

/// <summary>仪表盘概览 DTO。</summary>
public class DashboardDto
{
    [JsonPropertyName("subUrl")]
    public string SubUrl { get; set; } = string.Empty;

    [JsonPropertyName("enabledRuleCount")]
    public int EnabledRuleCount { get; set; }

    [JsonPropertyName("nodeCount")]
    public int NodeCount { get; set; }

    [JsonPropertyName("lastGoodUpdate")]
    public DateTimeOffset? LastGoodUpdate { get; set; }

    [JsonPropertyName("lastUpstreamUpdate")]
    public DateTimeOffset? LastUpstreamUpdate { get; set; }

    [JsonPropertyName("isStale")]
    public bool IsStale { get; set; }
}

/// <summary>配置视图 DTO。</summary>
public class ConfigDto
{
    [JsonPropertyName("yaml")]
    public string Yaml { get; set; } = string.Empty;

    [JsonPropertyName("lineCount")]
    public int LineCount { get; set; }

    [JsonPropertyName("ruleCount")]
    public int RuleCount { get; set; }

    [JsonPropertyName("lastGoodUpdate")]
    public DateTimeOffset? LastGoodUpdate { get; set; }

    [JsonPropertyName("isStale")]
    public bool IsStale { get; set; }
}

/// <summary>批量操作请求。</summary>
public class BatchRequest
{
    [JsonPropertyName("ids")]
    public Guid[]? Ids { get; set; }

    [JsonPropertyName("policy")]
    public string? Policy { get; set; }
}