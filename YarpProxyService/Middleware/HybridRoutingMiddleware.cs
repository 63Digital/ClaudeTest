using Microsoft.AspNetCore.Http.Extensions;
using YarpProxyService.BrowserPool;
using YarpProxyService.Routing;

namespace YarpProxyService.Middleware;

/// <summary>
/// Middleware that routes requests to either fast path (YARP) or browser path (Puppeteer).
/// </summary>
public class HybridRoutingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<HybridRoutingMiddleware> _logger;

    public HybridRoutingMiddleware(
        RequestDelegate next,
        ILogger<HybridRoutingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IntelligentRequestRouter router,
        BrowserFetchService browserFetchService)
    {
        // Make routing decision
        var decision = await router.DecideRouteAsync(context);

        // Add custom header to indicate routing path
        context.Response.Headers.Append("X-Proxy-Path", decision.UseFastPath ? "fast" : "browser");
        context.Response.Headers.Append("X-Proxy-Reason", decision.Reason);

        if (decision.UseFastPath)
        {
            // Fast path - let YARP handle it
            _logger.LogDebug("Using fast path for {Path}: {Reason}",
                context.Request.Path, decision.Reason);

            await _next(context);
        }
        else
        {
            // Browser path - fetch with headless browser
            _logger.LogInformation("Using browser path for {Path}: {Reason}",
                context.Request.Path, decision.Reason);

            await HandleBrowserPathAsync(context, browserFetchService);
        }
    }

    private async Task HandleBrowserPathAsync(HttpContext context, BrowserFetchService browserFetchService)
    {
        try
        {
            // Get or create session ID
            var sessionId = context.Session.Id;
            if (string.IsNullOrEmpty(sessionId))
            {
                await context.Session.LoadAsync();
                sessionId = context.Session.Id;
            }

            // Build the destination URL
            var destination = context.RequestServices
                .GetRequiredService<IConfiguration>()
                .GetValue<string>("ReverseProxy:Clusters:default-cluster:Destinations:primary:Address");

            if (string.IsNullOrEmpty(destination))
            {
                throw new Exception("Destination address not configured");
            }

            var targetUrl = $"{destination.TrimEnd('/')}{context.Request.Path}{context.Request.QueryString}";

            // Fetch using browser
            var result = await browserFetchService.FetchAsync(targetUrl, sessionId);

            if (result.Success && result.Content != null)
            {
                // Rewrite URLs in the content
                var rewrittenContent = RewriteUrls(result.Content, context.Request, destination);

                // Set response headers
                context.Response.StatusCode = result.StatusCode;
                context.Response.ContentType = "text/html; charset=utf-8";

                // Copy relevant headers
                if (result.Headers != null)
                {
                    foreach (var header in result.Headers)
                    {
                        if (ShouldCopyHeader(header.Key))
                        {
                            context.Response.Headers.TryAdd(header.Key, header.Value);
                        }
                    }
                }

                // Write content
                await context.Response.WriteAsync(rewrittenContent);
            }
            else
            {
                // Browser fetch failed
                context.Response.StatusCode = result.StatusCode;
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync(
                    result.ErrorMessage ?? "Browser fetch failed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in browser path handling");

            context.Response.StatusCode = 500;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync("Internal server error");
        }
    }

    private string RewriteUrls(string content, HttpRequest request, string destinationAddress)
    {
        try
        {
            if (!Uri.TryCreate(destinationAddress, UriKind.Absolute, out var destinationUri))
            {
                return content;
            }

            var destinationHost = destinationUri.Host;
            var proxyHost = request.Host.ToString();
            var proxyScheme = request.Scheme;

            // Rewrite absolute URLs
            content = System.Text.RegularExpressions.Regex.Replace(
                content,
                $@"https?://{System.Text.RegularExpressions.Regex.Escape(destinationHost)}",
                $"{proxyScheme}://{proxyHost}",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // Rewrite protocol-relative URLs
            content = content.Replace(
                $"//{destinationHost}",
                $"//{proxyHost}",
                StringComparison.OrdinalIgnoreCase);

            return content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rewriting URLs");
            return content;
        }
    }

    private bool ShouldCopyHeader(string headerName)
    {
        // Don't copy these headers
        var excludedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Content-Length",
            "Transfer-Encoding",
            "Content-Encoding",
            "Connection",
            "Keep-Alive"
        };

        return !excludedHeaders.Contains(headerName);
    }
}
