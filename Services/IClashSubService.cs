using ClashServer.Models;

namespace ClashServer.Services;

public interface IClashSubService
{
    Task<string> GetMergedSubAsync(string baseUrl, bool forceRefresh = false, CancellationToken ct = default);
    Task<bool> TestUpstreamAsync(CancellationToken ct = default);
    List<string> ParseYamlRules(string yamlRulesText);
    void ClearCache();
    Task<List<ProxyNode>> GetProxyNodesAsync(string baseUrl, bool forceRefresh = false, CancellationToken ct = default);
    Task<ProxyNode> TestNodeLatencyAsync(ProxyNode node, CancellationToken ct = default);
    DateTimeOffset? GetLastUpstreamUpdate();
    Task<string> GetRawUpstreamYamlAsync(bool forceRefresh = false, CancellationToken ct = default);
    Task<List<string>> GetProxyGroupsAsync(bool forceRefresh = false, CancellationToken ct = default);
}
