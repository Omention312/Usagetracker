namespace UsageTracker.Core.Normalize;

/// <summary>
/// URL → 规范化“站点”名。v2 2.2 规则:
///  - 所有子域/裸域折叠为 www.主域(公共后缀折叠);
///  - 特例表保证如 bilibili.com 及其全部子域 → www.bilibili.com。
/// 当前为主域启发式(取末两段) + 特例表;M1 阶段够用,M2 可换正式 PSL 实现。
/// </summary>
public static class HostNormalizer
{
    // 特例:这些“站点”的所有子域一律归并到该规范名(不限于两段,如 co.uk 等)
    private static readonly Dictionary<string, string> SpecialFolds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bilibili.com"] = "www.bilibili.com",
        ["youtube.com"] = "www.youtube.com",
        ["google.com"] = "www.google.com",
        ["zhihu.com"] = "www.zhihu.com",
        ["weibo.com"] = "www.weibo.com",
    };

    /// <summary>返回 null 表示非 http(s) URL(edge://、about:、文件等)。</summary>
    public static string? TryNormalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return null;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return null;

        string host = u.Host.ToLowerInvariant();
        if (host.Length == 0) return null;
        if (host == "localhost") return "localhost";

        foreach (var (domain, canon) in SpecialFolds)
        {
            if (host == domain || host.EndsWith("." + domain, StringComparison.Ordinal))
                return canon;
        }

        // 通用启发式:末两段为主域(不处理 co.uk 等;特例表已覆盖常见)。IPv4 直接返回。
        if (System.Net.IPAddress.TryParse(host, out _)) return host;
        var parts = host.Split('.');
        if (parts.Length <= 2) return "www." + host;
        return "www." + parts[^2] + "." + parts[^1];
    }
}
