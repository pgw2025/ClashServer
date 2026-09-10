using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ClashServer.Models;

public class CustomRule
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("ruleType")]
    [Required]
    [Display(Name = "规则类型")]
    public string RuleType { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    [Required]
    [Display(Name = "匹配内容")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("policy")]
    [Required]
    [Display(Name = "策略")]
    public string Policy { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    [Display(Name = "启用")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("remark")]
    [Display(Name = "备注")]
    public string? Remark { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public static readonly string[] AvailableRuleTypes = new[]
    {
        "DOMAIN",
        "DOMAIN-SUFFIX",
        "DOMAIN-KEYWORD",
        "GEOIP",
        "IP-CIDR",
        "IP-CIDR6",
        "IP-ASN",
        "SRC-IP-CIDR",
        "SRC-PORT",
        "DST-PORT",
        "PROCESS-NAME",
        "PROCESS-PATH",
        "NETWORK",
        "UID",
        "IN-PORT",
        "IN-TYPE",
        "IN-USER",
        "IN-NAME",
        "DSCP",
        "RULE-SET",
        "GEOSITE",
        "GEODATA",
        "MATCH"
    };

    public static readonly string[] AvailablePolicies = new[]
    {
        "DIRECT",
        "REJECT",
        "PROXY",
        "REJECT-DROP",
        "PASS",
        "no-resolve"
    };

    public string ToYamlLine()
    {
        var parts = new List<string> { RuleType, Target, Policy };
        if (!string.IsNullOrWhiteSpace(Remark))
        {
            parts.Add($"# {Remark}");
        }
        return string.Join(",", parts);
    }
}
