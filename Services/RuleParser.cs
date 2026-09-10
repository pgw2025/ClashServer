using ClashServer.Models;

namespace ClashServer.Services;

/// <summary>把 YAML 规则文本解析为 CustomRule 列表（复用于导入预览与 SPA parse 端点）。</summary>
public static class RuleParser
{
    public static List<CustomRule> ParseText(string yamlText)
    {
        var knownTypes = new HashSet<string>(CustomRule.AvailableRuleTypes, StringComparer.OrdinalIgnoreCase);
        var validPolicies = new HashSet<string>(CustomRule.AvailablePolicies, StringComparer.OrdinalIgnoreCase)
        {
            "Proxy", "代理", "Direct", "Reject", "No-Resolve"
        };

        var result = new List<CustomRule>();
        foreach (var line in ParseYamlLines(yamlText))
        {
            var rule = ParseLine(line, knownTypes, validPolicies);
            if (rule != null) result.Add(rule);
        }
        return result;
    }

    private static List<string> ParseYamlLines(string yamlRulesText)
    {
        if (string.IsNullOrWhiteSpace(yamlRulesText)) return new();
        return yamlRulesText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static CustomRule? ParseLine(string line, HashSet<string> validTypes, HashSet<string> validPolicies)
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

        var ruleType = parts[0];
        var target = parts[1];
        var policy = parts.Length >= 3 ? parts[2] : "PROXY";

        if (!validTypes.Contains(ruleType)) return null;

        return new CustomRule
        {
            RuleType = ruleType,
            Target = target,
            Policy = policy,
            Enabled = true,
            Remark = remark
        };
    }
}