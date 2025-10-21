using PuppeteerSharp;

namespace YarpProxyService.BrowserPool;

/// <summary>
/// Service for fetching web pages using headless browser.
/// </summary>
public class BrowserFetchService
{
    private readonly BrowserPoolManager _poolManager;
    private readonly ILogger<BrowserFetchService> _logger;
    private readonly IConfiguration _configuration;

    public BrowserFetchService(
        BrowserPoolManager poolManager,
        ILogger<BrowserFetchService> logger,
        IConfiguration configuration)
    {
        _poolManager = poolManager;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Fetches a URL using a headless browser.
    /// </summary>
    public async Task<BrowserFetchResult> FetchAsync(string url, string sessionId)
    {
        BrowserInstance? browser = null;

        try
        {
            _logger.LogInformation("Fetching {Url} with browser for session {SessionId}", url, sessionId);

            // Acquire browser from pool
            browser = await _poolManager.AcquireAsync(sessionId);

            // Navigate to the URL
            var navigationOptions = new NavigationOptions
            {
                Timeout = 30000, // 30 seconds
                WaitUntil = new[] { WaitUntilNavigation.Networkidle2 }
            };

            var response = await browser.Page.GoToAsync(url, navigationOptions);

            if (response == null)
            {
                throw new Exception("Navigation failed - no response");
            }

            // Wait a bit for any dynamic content to load
            await Task.Delay(1000);

            // Get the rendered HTML
            var content = await browser.Page.GetContentAsync();

            // Get response status and headers
            var statusCode = response.Status;
            var headers = response.Headers;

            _logger.LogInformation(
                "Successfully fetched {Url} with browser. Status: {Status}, Content length: {Length}",
                url,
                statusCode,
                content.Length);

            return new BrowserFetchResult
            {
                Success = true,
                Content = content,
                StatusCode = statusCode,
                Headers = headers,
                BrowserInstanceId = browser.Id,
                Url = response.Url
            };
        }
        catch (BrowserPoolExhaustedException ex)
        {
            _logger.LogWarning(ex, "Browser pool exhausted while fetching {Url}", url);

            return new BrowserFetchResult
            {
                Success = false,
                ErrorMessage = "Browser pool at capacity. Please try again later.",
                StatusCode = 503
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching {Url} with browser", url);

            return new BrowserFetchResult
            {
                Success = false,
                ErrorMessage = $"Browser fetch failed: {ex.Message}",
                StatusCode = 500
            };
        }
    }

    /// <summary>
    /// Releases a browser session.
    /// </summary>
    public async Task ReleaseSessionAsync(string sessionId)
    {
        try
        {
            await _poolManager.ReleaseAsync(sessionId);
            _logger.LogDebug("Released browser session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error releasing session {SessionId}", sessionId);
        }
    }
}

/// <summary>
/// Result of a browser fetch operation.
/// </summary>
public class BrowserFetchResult
{
    public bool Success { get; set; }
    public string? Content { get; set; }
    public int StatusCode { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public string? ErrorMessage { get; set; }
    public string? BrowserInstanceId { get; set; }
    public string? Url { get; set; }
}
