using System.Text.RegularExpressions;
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
        foreach (var line in PreprocessLines(yamlText))
        {
            var rule = ParseLine(line, knownTypes, validPolicies);
            if (rule != null) result.Add(rule);
        }
        return result;
    }

    /// <summary>从原始文本提取规则行：识别 rules: 块、剥离 YAML 列表前缀 - ，纯文本兜底（复用于 SPA parse 与 Razor 导入）。</summary>
    public static List<string> PreprocessLines(string yamlRulesText)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(yamlRulesText)) return result;

        var lines = yamlRulesText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var inRulesBlock = false;
        var rulesIndent = -1;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line)) continue;
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith('#'))
            {
                if (inRulesBlock) continue;
                continue;
            }

            if (!inRulesBlock)
            {
                if (Regex.IsMatch(trimmed, @"^rules\s*:"))
                {
                    inRulesBlock = true;
                    rulesIndent = rawLine.Length - trimmed.Length;
                    continue;
                }
                continue;
            }

            var currentIndent = rawLine.Length - trimmed.Length;
            if (currentIndent <= rulesIndent && trimmed.Length > 0)
            {
                if (!trimmed.StartsWith("-"))
                {
                    inRulesBlock = false;
                    continue;
                }
            }

            if (trimmed.StartsWith("- "))
            {
                var ruleText = trimmed[2..].Trim();
                if (!string.IsNullOrWhiteSpace(ruleText))
                {
                    result.Add(ruleText);
                }
            }
            else if (trimmed.StartsWith('-'))
            {
                var ruleText = trimmed[1..].Trim();
                if (!string.IsNullOrWhiteSpace(ruleText))
                {
                    result.Add(ruleText);
                }
            }
        }

        if (result.Count == 0)
        {
            foreach (var rawLine in lines)
            {
                var trimmed = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#')) continue;
                if (Regex.IsMatch(trimmed, @"^rules\s*:")) continue;
                if (trimmed.StartsWith("- "))
                {
                    result.Add(trimmed[2..].Trim());
                }
                else if (trimmed.Contains(',') && trimmed.Length > 5)
                {
                    result.Add(trimmed);
                }
            }
        }

        return result;
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