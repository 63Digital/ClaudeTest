using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Yarp.ReverseProxy.Transforms;

namespace YarpProxyService.Transforms;

/// <summary>
/// Transform that rewrites URLs in response content to redirect through the proxy.
/// </summary>
public class ResponseRewriteTransform : ResponseTransform
{
    private readonly string _destinationHost;
    private readonly ILogger<ResponseRewriteTransform> _logger;
    private readonly HashSet<string> _rewritableContentTypes;
    private readonly Regex _absoluteUrlRegex;
    private readonly Regex _protocolRelativeUrlRegex;

    public ResponseRewriteTransform(
        string destinationHost,
        ILogger<ResponseRewriteTransform> logger,
        IConfiguration configuration)
    {
        _destinationHost = destinationHost;
        _logger = logger;

        // Load rewritable content types from configuration
        _rewritableContentTypes = configuration.GetSection("UrlRewriting:RewriteContentTypes")
            .Get<HashSet<string>>() ?? new HashSet<string>
            {
                "text/html",
                "text/css",
                "application/javascript",
                "text/javascript",
                "application/json"
            };

        // Compile regex patterns for better performance
        var escapedHost = Regex.Escape(_destinationHost);
        _absoluteUrlRegex = new Regex(
            $@"https?://{escapedHost}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        _protocolRelativeUrlRegex = new Regex(
            $@"//{escapedHost}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }

    public override async ValueTask ApplyAsync(ResponseTransformContext context)
    {
        // First, handle Location header rewriting for redirects
        if (context.HttpContext.Response.Headers.TryGetValue("Location", out var locationValues))
        {
            var location = locationValues.ToString();
            var rewrittenLocation = RewriteUrl(location, context.HttpContext.Request);

            if (location != rewrittenLocation)
            {
                context.HttpContext.Response.Headers["Location"] = rewrittenLocation;
                _logger.LogDebug("Rewrote Location header from {Original} to {Rewritten}",
                    location, rewrittenLocation);
            }
        }

        // Check if URL rewriting is enabled
        var rewritingEnabled = context.HttpContext.RequestServices
            .GetRequiredService<IConfiguration>()
            .GetValue<bool>("UrlRewriting:Enabled", true);

        if (!rewritingEnabled)
        {
            return;
        }

        // Check if content type is rewritable
        var contentType = context.HttpContext.Response.ContentType;
        if (string.IsNullOrEmpty(contentType) || !ShouldRewriteContentType(contentType))
        {
            _logger.LogDebug("Skipping URL rewriting for content type: {ContentType}", contentType);
            return;
        }

        // Only rewrite successful responses
        var statusCode = context.HttpContext.Response.StatusCode;
        if (statusCode < 200 || statusCode >= 300)
        {
            _logger.LogDebug("Skipping URL rewriting for status code: {StatusCode}", statusCode);
            return;
        }

        try
        {
            // Read the response body
            var originalBody = context.HttpContext.Response.Body;
            using var memoryStream = new MemoryStream();
            context.HttpContext.Response.Body = memoryStream;

            // Let YARP write the proxied response to our memory stream
            await base.ApplyAsync(context);

            // Read and rewrite the content
            memoryStream.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(memoryStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var content = await reader.ReadToEndAsync();

            var rewrittenContent = RewriteContent(content, context.HttpContext.Request);

            // Write the rewritten content back
            var rewrittenBytes = Encoding.UTF8.GetBytes(rewrittenContent);
            context.HttpContext.Response.Body = originalBody;
            context.HttpContext.Response.ContentLength = rewrittenBytes.Length;

            await originalBody.WriteAsync(rewrittenBytes);

            _logger.LogDebug(
                "Rewrote response content. Original size: {OriginalSize}, New size: {NewSize}",
                memoryStream.Length,
                rewrittenBytes.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rewriting response content. Returning original content.");
            // On error, let the original response through
            await base.ApplyAsync(context);
        }
    }

    /// <summary>
    /// Rewrites URLs in the content to point to the proxy server.
    /// </summary>
    private string RewriteContent(string content, HttpRequest request)
    {
        var proxyHost = request.Host.ToString();
        var proxyScheme = request.Scheme;

        // Rewrite absolute URLs (https://destination.com -> http://proxy-host)
        content = _absoluteUrlRegex.Replace(content, $"{proxyScheme}://{proxyHost}");

        // Rewrite protocol-relative URLs (//destination.com -> //proxy-host)
        content = _protocolRelativeUrlRegex.Replace(content, $"//{proxyHost}");

        return content;
    }

    /// <summary>
    /// Rewrites a single URL (used for Location headers).
    /// </summary>
    private string RewriteUrl(string url, HttpRequest request)
    {
        if (string.IsNullOrEmpty(url))
            return url;

        var proxyHost = request.Host.ToString();
        var proxyScheme = request.Scheme;

        // Rewrite absolute URLs
        url = _absoluteUrlRegex.Replace(url, $"{proxyScheme}://{proxyHost}");

        // Rewrite protocol-relative URLs
        url = _protocolRelativeUrlRegex.Replace(url, $"//{proxyHost}");

        return url;
    }

    /// <summary>
    /// Checks if the content type should be rewritten.
    /// </summary>
    private bool ShouldRewriteContentType(string contentType)
    {
        // Parse the content type to get just the media type (without charset, etc.)
        if (MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
        {
            var mediaTypeString = mediaType.MediaType?.ToLowerInvariant();
            return mediaTypeString != null && _rewritableContentTypes.Contains(mediaTypeString);
        }

        return false;
    }
}
