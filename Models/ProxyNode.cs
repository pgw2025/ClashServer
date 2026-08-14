namespace ClashServer.Models;

public class ProxyNode
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public int Port { get; set; }
    public int? Latency { get; set; }
    public string? Error { get; set; }

    public string TypeBadgeColor => Type switch
    {
        "ss" => "primary",
        "ssr" => "secondary",
        "vmess" => "success",
        "vless" => "info",
        "trojan" => "warning",
        "hysteria" or "hysteria2" => "danger",
        "tuic" => "dark",
        _ => "light"
    };

    public string LatencyColor => Latency switch
    {
        null => "secondary",
        < 100 => "success",
        < 300 => "primary",
        < 1000 => "warning",
        _ => "danger"
    };
}
