using ClashServer.Models;

namespace ClashServer.Services;

public record SubFetchResult(string Yaml, bool Degraded, DateTimeOffset? DataAt);

public record RawFetchResult(string Yaml, bool Degraded);

public interface IClashSubService
{
    Task<string> GetMergedSubAsync(string baseUrl, bool forceRefresh = false, CancellationToken ct = default, TimeSpan? fetchTimeout = null);
    Task<SubFetchResult> GetMergedSubSafeAsync(string baseUrl, bool forceRefresh = false, CancellationToken ct = default, TimeSpan? fetchTimeout = null);
    Task<bool> TestUpstreamAsync(CancellationToken ct = default);
    List<string> ParseYamlRules(string yamlRulesText);
    void ClearCache();
    Task<List<ProxyNode>> GetProxyNodesAsync(string baseUrl, bool forceRefresh = false, CancellationToken ct = default, TimeSpan? fetchTimeout = null);
    Task<ProxyNode> TestNodeLatencyAsync(ProxyNode node, CancellationToken ct = default);
    DateTimeOffset? GetLastUpstreamUpdate();
    DateTimeOffset? GetLastGoodUpdate();
    List<ProxyNode>? GetCachedNodes();
    List<string>? GetCachedGroups();
    Task<string> GetRawUpstreamYamlAsync(bool forceRefresh = false, CancellationToken ct = default, TimeSpan? fetchTimeout = null);
    Task<List<string>> GetProxyGroupsAsync(bool forceRefresh = false, CancellationToken ct = default, TimeSpan? fetchTimeout = null);
}
