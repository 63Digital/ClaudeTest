namespace YarpProxyService.Routing;

/// <summary>
/// Represents a routing decision for a request.
/// </summary>
public class RouteDecision
{
    /// <summary>
    /// True if the request should use the fast path (YARP URL rewriting).
    /// False if it should use the browser path (Puppeteer).
    /// </summary>
    public bool UseFastPath { get; set; }

    /// <summary>
    /// Reason for the routing decision.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Whether this decision should be learned/cached for future requests.
    /// </summary>
    public bool ShouldLearn { get; set; }
}
