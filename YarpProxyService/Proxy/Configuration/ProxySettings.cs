namespace YarpProxyService.Proxy.Configuration;

/// <summary>
/// Configuration settings for the external proxy server.
/// </summary>
public class ProxySettings
{
    /// <summary>
    /// The URI of the proxy server (e.g., http://proxy-server:port).
    /// </summary>
    public string Uri { get; set; } = string.Empty;

    /// <summary>
    /// Username for proxy authentication.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Password for proxy authentication.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// The type of proxy (Http, Https, or Socks5).
    /// </summary>
    public ProxyType Type { get; set; } = ProxyType.Http;

    /// <summary>
    /// Timeout in seconds for proxy connections.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Supported proxy types.
/// </summary>
public enum ProxyType
{
    /// <summary>
    /// HTTP proxy.
    /// </summary>
    Http,

    /// <summary>
    /// HTTPS proxy.
    /// </summary>
    Https,

    /// <summary>
    /// SOCKS5 proxy (future support).
    /// </summary>
    Socks5
}
