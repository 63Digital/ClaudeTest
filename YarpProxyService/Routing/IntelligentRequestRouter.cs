using YarpProxyService.BrowserPool;

namespace YarpProxyService.Routing;

/// <summary>
/// Decides whether to route a request through the fast path (YARP rewriting)
/// or the browser path (Puppeteer rendering).
/// </summary>
public class IntelligentRequestRouter
{
    private readonly ILogger<IntelligentRequestRouter> _logger;
    private readonly IConfiguration _configuration;
    private readonly BrowserPoolManager _browserPoolManager;

    // Known patterns for different content types
    private static readonly HashSet<string> StaticExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".svg", ".ico",
        ".css", ".js", ".woff", ".woff2", ".ttf", ".eot",
        ".pdf", ".zip", ".mp4", ".mp3"
    };

    private static readonly HashSet<string> SpaIndicators = new(StringComparer.OrdinalIgnoreCase)
    {
        "/app", "/dashboard", "/admin", "/portal"
    };

    public IntelligentRequestRouter(
        ILogger<IntelligentRequestRouter> logger,
        IConfiguration configuration,
        BrowserPoolManager browserPoolManager)
    {
        _logger = logger;
        _configuration = configuration;
        _browserPoolManager = browserPoolManager;
    }

    /// <summary>
    /// Decides the routing path for a request.
    /// </summary>
    public Task<RouteDecision> DecideRouteAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";
        var decision = new RouteDecision();

        // Always use fast path for static content
        if (IsStaticContent(path))
        {
            decision.UseFastPath = true;
            decision.Reason = "Static content - using fast path";
            _logger.LogDebug("Route decision for {Path}: {Reason}", path, decision.Reason);
            return Task.FromResult(decision);
        }

        // Check if browser path is enabled
        var browserPathEnabled = _configuration.GetValue("Scaling:BrowserPath:Enabled", false);
        if (!browserPathEnabled)
        {
            decision.UseFastPath = true;
            decision.Reason = "Browser path disabled - using fast path";
            _logger.LogDebug("Route decision for {Path}: {Reason}", path, decision.Reason);
            return Task.FromResult(decision);
        }

        // Check if path looks like a SPA route
        if (IsSPARoute(path))
        {
            // Check browser pool capacity
            var poolUtilization = _browserPoolManager.ActiveInstances / (double)_browserPoolManager.TotalInstances;

            if (poolUtilization >= 0.9) // 90% capacity
            {
                decision.UseFastPath = true;
                decision.Reason = "Browser pool at capacity - fallback to fast path";
                _logger.LogWarning("Route decision for {Path}: {Reason} (utilization: {Utilization:P0})",
                    path, decision.Reason, poolUtilization);
            }
            else
            {
                decision.UseFastPath = false;
                decision.Reason = "SPA route - using browser path";
                _logger.LogDebug("Route decision for {Path}: {Reason}", path, decision.Reason);
            }

            return Task.FromResult(decision);
        }

        // Check for specific query parameters that might indicate dynamic content
        if (context.Request.Query.ContainsKey("react") ||
            context.Request.Query.ContainsKey("vue") ||
            context.Request.Query.ContainsKey("angular"))
        {
            decision.UseFastPath = false;
            decision.Reason = "Dynamic framework detected - using browser path";
            _logger.LogDebug("Route decision for {Path}: {Reason}", path, decision.Reason);
            return Task.FromResult(decision);
        }

        // Default: use fast path
        decision.UseFastPath = true;
        decision.Reason = "Default routing - using fast path";
        decision.ShouldLearn = true;
        _logger.LogDebug("Route decision for {Path}: {Reason}", path, decision.Reason);

        return Task.FromResult(decision);
    }

    /// <summary>
    /// Checks if the path is for static content.
    /// </summary>
    private bool IsStaticContent(string path)
    {
        var extension = Path.GetExtension(path);
        return !string.IsNullOrEmpty(extension) && StaticExtensions.Contains(extension);
    }

    /// <summary>
    /// Checks if the path looks like a Single Page Application route.
    /// </summary>
    private bool IsSPARoute(string path)
    {
        // Root path
        if (path == "/") return true;

        // Known SPA paths
        if (SpaIndicators.Any(indicator => path.StartsWith(indicator, StringComparison.OrdinalIgnoreCase)))
            return true;

        // Path without extension and shallow depth (likely a route, not a file)
        if (string.IsNullOrEmpty(Path.GetExtension(path)) && path.Split('/').Length <= 3)
            return true;

        return false;
    }
}
