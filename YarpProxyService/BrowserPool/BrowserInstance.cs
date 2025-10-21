using PuppeteerSharp;

namespace YarpProxyService.BrowserPool;

/// <summary>
/// Represents a single browser instance in the pool.
/// </summary>
public class BrowserInstance : IDisposable
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public IBrowser Browser { get; set; } = null!;
    public IPage Page { get; set; } = null!;
    public string? SessionId { get; set; }
    public DateTime LastUsed { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsHealthy { get; private set; } = true;

    /// <summary>
    /// Resets the browser instance to a clean state.
    /// </summary>
    public async Task ResetAsync()
    {
        try
        {
            // Clear browser state
            if (Page != null && !Page.IsClosed)
            {
                // Clear storage
                var client = await Page.Target.CreateCDPSessionAsync();
                await client.SendAsync("Network.clearBrowserCookies");
                await client.SendAsync("Network.clearBrowserCache");

                // Clear local/session storage
                await Page.EvaluateFunctionAsync("() => { localStorage.clear(); sessionStorage.clear(); }");

                // Navigate to blank page
                await Page.GoToAsync("about:blank");
            }

            SessionId = null;
            LastUsed = DateTime.UtcNow;
            IsHealthy = true;
        }
        catch (Exception)
        {
            IsHealthy = false;
            throw;
        }
    }

    /// <summary>
    /// Checks if the browser instance is still healthy.
    /// </summary>
    public async Task<bool> CheckHealthAsync()
    {
        try
        {
            if (Browser == null || !Browser.IsConnected)
            {
                IsHealthy = false;
                return false;
            }

            if (Page == null || Page.IsClosed)
            {
                IsHealthy = false;
                return false;
            }

            // Try a simple operation
            await Page.EvaluateFunctionAsync("() => true");
            IsHealthy = true;
            return true;
        }
        catch
        {
            IsHealthy = false;
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            Page?.CloseAsync().GetAwaiter().GetResult();
            Browser?.CloseAsync().GetAwaiter().GetResult();
            Browser?.Dispose();
        }
        catch
        {
            // Ignore disposal errors
        }
    }
}
