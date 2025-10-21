using System.Collections.Concurrent;
using PuppeteerSharp;

namespace YarpProxyService.BrowserPool;

/// <summary>
/// Manages a pool of browser instances for handling complex JavaScript-heavy sites.
/// </summary>
public class BrowserPoolManager : IDisposable
{
    private readonly ILogger<BrowserPoolManager> _logger;
    private readonly IConfiguration _configuration;
    private readonly ConcurrentBag<BrowserInstance> _availableBrowsers = new();
    private readonly ConcurrentDictionary<string, BrowserInstance> _activeSessions = new();
    private readonly SemaphoreSlim _poolSemaphore;
    private readonly int _maxPoolSize;
    private readonly int _minPoolSize;
    private readonly Timer _maintenanceTimer;
    private bool _isDisposed;

    public int TotalInstances => _availableBrowsers.Count + _activeSessions.Count;
    public int AvailableInstances => _availableBrowsers.Count;
    public int ActiveInstances => _activeSessions.Count;

    public BrowserPoolManager(
        ILogger<BrowserPoolManager> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        _maxPoolSize = configuration.GetValue("BrowserPool:MaxSize", 10);
        _minPoolSize = configuration.GetValue("BrowserPool:MinSize", 2);

        _poolSemaphore = new SemaphoreSlim(_maxPoolSize, _maxPoolSize);

        // Start maintenance timer (runs every 2 minutes)
        _maintenanceTimer = new Timer(
            async _ => await MaintenanceAsync(),
            null,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(2));

        _logger.LogInformation(
            "Browser pool initialized. Min: {Min}, Max: {Max}",
            _minPoolSize,
            _maxPoolSize);
    }

    /// <summary>
    /// Initializes the browser pool with minimum instances.
    /// </summary>
    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing browser pool...");

        // Download Chromium if not present
        await new BrowserFetcher().DownloadAsync();

        // Create minimum number of browsers
        var tasks = new List<Task>();
        for (int i = 0; i < _minPoolSize; i++)
        {
            tasks.Add(CreateAndAddBrowserAsync());
        }

        await Task.WhenAll(tasks);

        _logger.LogInformation(
            "Browser pool initialized with {Count} instances",
            _availableBrowsers.Count);
    }

    /// <summary>
    /// Acquires a browser instance for a session.
    /// </summary>
    public async Task<BrowserInstance> AcquireAsync(string sessionId)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(BrowserPoolManager));

        // Check if session already has a browser
        if (_activeSessions.TryGetValue(sessionId, out var existing))
        {
            existing.LastUsed = DateTime.UtcNow;
            _logger.LogDebug("Reusing browser {BrowserId} for session {SessionId}", existing.Id, sessionId);
            return existing;
        }

        // Try to get from pool
        if (_availableBrowsers.TryTake(out var browser))
        {
            browser.SessionId = sessionId;
            browser.LastUsed = DateTime.UtcNow;
            _activeSessions[sessionId] = browser;

            _logger.LogDebug("Assigned browser {BrowserId} to session {SessionId}", browser.Id, sessionId);
            return browser;
        }

        // Pool is empty - check if we can create more
        if (TotalInstances < _maxPoolSize)
        {
            await _poolSemaphore.WaitAsync();
            try
            {
                var newBrowser = await CreateBrowserInstanceAsync();
                newBrowser.SessionId = sessionId;
                newBrowser.LastUsed = DateTime.UtcNow;
                _activeSessions[sessionId] = newBrowser;

                _logger.LogInformation(
                    "Created new browser {BrowserId} for session {SessionId}. Total: {Total}",
                    newBrowser.Id,
                    sessionId,
                    TotalInstances);

                return newBrowser;
            }
            finally
            {
                _poolSemaphore.Release();
            }
        }

        // Pool at capacity
        throw new BrowserPoolExhaustedException(
            $"Browser pool at capacity ({_maxPoolSize}). Try again later.");
    }

    /// <summary>
    /// Releases a browser back to the pool.
    /// </summary>
    public async Task ReleaseAsync(string sessionId)
    {
        if (_activeSessions.TryRemove(sessionId, out var browser))
        {
            try
            {
                await browser.ResetAsync();
                browser.SessionId = null;
                _availableBrowsers.Add(browser);

                _logger.LogDebug("Released browser {BrowserId} from session {SessionId}", browser.Id, sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset browser {BrowserId}. Disposing.", browser.Id);
                browser.Dispose();
                _poolSemaphore.Release();
            }
        }
    }

    /// <summary>
    /// Creates a new browser instance.
    /// </summary>
    private async Task<BrowserInstance> CreateBrowserInstanceAsync()
    {
        var proxyUri = _configuration.GetValue<string>("Proxy:Uri");
        var proxyUsername = _configuration.GetValue<string>("Proxy:Username");
        var proxyPassword = _configuration.GetValue<string>("Proxy:Password");

        var launchOptions = new LaunchOptions
        {
            Headless = true,
            Args = new[]
            {
                "--no-sandbox",
                "--disable-setuid-sandbox",
                "--disable-dev-shm-usage",
                "--disable-accelerated-2d-canvas",
                "--no-first-run",
                "--no-zygote",
                "--disable-gpu"
            }
        };

        // Add proxy if configured
        if (!string.IsNullOrEmpty(proxyUri))
        {
            launchOptions.Args = launchOptions.Args.Append($"--proxy-server={proxyUri}").ToArray();
        }

        var browser = await Puppeteer.LaunchAsync(launchOptions);
        var page = await browser.NewPageAsync();

        // Set proxy authentication if configured
        if (!string.IsNullOrEmpty(proxyUsername) && !string.IsNullOrEmpty(proxyPassword))
        {
            await page.AuthenticateAsync(new Credentials
            {
                Username = proxyUsername,
                Password = proxyPassword
            });
        }

        // Set reasonable timeouts
        page.DefaultTimeout = 30000; // 30 seconds

        return new BrowserInstance
        {
            Browser = browser,
            Page = page,
            CreatedAt = DateTime.UtcNow,
            LastUsed = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates and adds a browser to the pool.
    /// </summary>
    private async Task CreateAndAddBrowserAsync()
    {
        try
        {
            var browser = await CreateBrowserInstanceAsync();
            _availableBrowsers.Add(browser);
            _logger.LogDebug("Added browser {BrowserId} to pool", browser.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create browser instance");
        }
    }

    /// <summary>
    /// Periodic maintenance: cleanup idle sessions and maintain minimum pool size.
    /// </summary>
    private async Task MaintenanceAsync()
    {
        if (_isDisposed) return;

        try
        {
            _logger.LogDebug(
                "Running maintenance. Available: {Available}, Active: {Active}, Total: {Total}",
                AvailableInstances,
                ActiveInstances,
                TotalInstances);

            // Cleanup idle sessions (idle for more than 15 minutes)
            var idleTimeout = TimeSpan.FromMinutes(15);
            var now = DateTime.UtcNow;

            var idleSessions = _activeSessions
                .Where(kvp => now - kvp.Value.LastUsed > idleTimeout)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var sessionId in idleSessions)
            {
                _logger.LogInformation("Cleaning up idle session {SessionId}", sessionId);
                await ReleaseAsync(sessionId);
            }

            // Remove unhealthy browsers from available pool
            var unhealthyBrowsers = new List<BrowserInstance>();
            var healthyBrowsers = new List<BrowserInstance>();

            while (_availableBrowsers.TryTake(out var browser))
            {
                if (await browser.CheckHealthAsync())
                {
                    healthyBrowsers.Add(browser);
                }
                else
                {
                    unhealthyBrowsers.Add(browser);
                    _logger.LogWarning("Found unhealthy browser {BrowserId}", browser.Id);
                }
            }

            // Put healthy browsers back
            foreach (var browser in healthyBrowsers)
            {
                _availableBrowsers.Add(browser);
            }

            // Dispose unhealthy browsers
            foreach (var browser in unhealthyBrowsers)
            {
                browser.Dispose();
                _poolSemaphore.Release();
            }

            // Maintain minimum pool size
            while (_availableBrowsers.Count < _minPoolSize && TotalInstances < _maxPoolSize)
            {
                await CreateAndAddBrowserAsync();
            }

            _logger.LogDebug("Maintenance complete. Available: {Available}, Active: {Active}",
                AvailableInstances, ActiveInstances);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during browser pool maintenance");
        }
    }

    /// <summary>
    /// Closes all browser instances.
    /// </summary>
    public async Task CloseAllAsync()
    {
        _logger.LogInformation("Closing all browser instances...");

        var allBrowsers = _availableBrowsers.Concat(_activeSessions.Values).ToList();

        foreach (var browser in allBrowsers)
        {
            try
            {
                browser.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing browser {BrowserId}", browser.Id);
            }
        }

        _availableBrowsers.Clear();
        _activeSessions.Clear();

        _logger.LogInformation("All browser instances closed");
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        _isDisposed = true;
        _maintenanceTimer?.Dispose();
        CloseAllAsync().GetAwaiter().GetResult();
        _poolSemaphore?.Dispose();
    }
}

/// <summary>
/// Exception thrown when the browser pool is at capacity.
/// </summary>
public class BrowserPoolExhaustedException : Exception
{
    public BrowserPoolExhaustedException(string message) : base(message) { }
}
