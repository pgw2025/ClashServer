namespace ClashServer.Services;

public interface IClashSubService
{
    Task<string> GetMergedSubAsync(string baseUrl, bool forceRefresh = false, CancellationToken ct = default);
    Task<bool> TestUpstreamAsync(CancellationToken ct = default);
    List<string> ParseYamlRules(string yamlRulesText);
    void ClearCache();
}
