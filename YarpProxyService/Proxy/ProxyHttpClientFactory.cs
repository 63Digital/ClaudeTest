using System.Net;
using Yarp.ReverseProxy.Forwarder;
using YarpProxyService.Proxy.Configuration;

namespace YarpProxyService.Proxy;

/// <summary>
/// Custom HTTP client factory that configures the forwarder to use an external proxy.
/// </summary>
public class ProxyHttpClientFactory : IForwarderHttpClientFactory
{
    private readonly ILogger<ProxyHttpClientFactory> _logger;
    private readonly ProxySettings _proxySettings;

    public ProxyHttpClientFactory(
        ILogger<ProxyHttpClientFactory> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _proxySettings = configuration.GetSection("Proxy").Get<ProxySettings>()
            ?? throw new InvalidOperationException("Proxy configuration section is missing or invalid.");

        ValidateProxySettings();
    }

    /// <summary>
    /// Creates an HttpMessageInvoker configured to use the external proxy.
    /// </summary>
    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = true,
            Proxy = CreateWebProxy(),
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(_proxySettings.TimeoutSeconds),
            ActivityHeadersPropagator = new ReverseProxyPropagator(DistributedContextPropagator.Current),
            // Enable connection pooling
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5)
        };

        _logger.LogInformation(
            "Created HTTP client with proxy: {ProxyUri}, Type: {ProxyType}",
            _proxySettings.Uri,
            _proxySettings.Type);

        return new HttpMessageInvoker(handler, disposeHandler: true);
    }

    /// <summary>
    /// Creates a configured WebProxy instance.
    /// </summary>
    private WebProxy CreateWebProxy()
    {
        var proxy = new WebProxy(_proxySettings.Uri, BypassOnLocal: false);

        // Configure authentication if credentials are provided
        if (!string.IsNullOrEmpty(_proxySettings.Username) && !string.IsNullOrEmpty(_proxySettings.Password))
        {
            proxy.Credentials = new NetworkCredential(_proxySettings.Username, _proxySettings.Password);
            _logger.LogDebug("Proxy authentication configured for user: {Username}", _proxySettings.Username);
        }
        else
        {
            _logger.LogDebug("No proxy authentication configured");
        }

        return proxy;
    }

    /// <summary>
    /// Validates the proxy settings on startup.
    /// </summary>
    private void ValidateProxySettings()
    {
        if (string.IsNullOrEmpty(_proxySettings.Uri))
        {
            throw new InvalidOperationException("Proxy URI is required but not configured.");
        }

        if (!Uri.TryCreate(_proxySettings.Uri, UriKind.Absolute, out var proxyUri))
        {
            throw new InvalidOperationException($"Invalid proxy URI format: {_proxySettings.Uri}");
        }

        if (_proxySettings.Type == ProxyType.Socks5)
        {
            _logger.LogWarning("SOCKS5 proxy support is not yet implemented. Treating as HTTP proxy.");
        }

        if (_proxySettings.TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("Proxy timeout must be greater than 0 seconds.");
        }

        _logger.LogInformation(
            "Proxy settings validated successfully. URI: {Uri}, Type: {Type}, Timeout: {Timeout}s",
            _proxySettings.Uri,
            _proxySettings.Type,
            _proxySettings.TimeoutSeconds);
    }
}

/// <summary>
/// Custom propagator for distributed tracing headers in reverse proxy scenarios.
/// </summary>
internal class ReverseProxyPropagator : DistributedContextPropagator
{
    private readonly DistributedContextPropagator _inner;

    public ReverseProxyPropagator(DistributedContextPropagator inner)
    {
        _inner = inner;
    }

    public override IReadOnlyCollection<string> Fields => _inner.Fields;

    public override void Inject(Activity? activity, object? carrier, PropagatorSetterCallback? setter)
    {
        _inner.Inject(activity, carrier, setter);
    }

    public override void Extract(object? carrier, PropagatorGetterCallback? getter, out string? traceParent, out string? traceState)
    {
        _inner.Extract(carrier, getter, out traceParent, out traceState);
    }
}
