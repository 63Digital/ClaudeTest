using Yarp.ReverseProxy.Forwarder;
using YarpProxyService.Proxy;
using YarpProxyService.Transforms;
using YarpProxyService.BrowserPool;
using YarpProxyService.Routing;
using YarpProxyService.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Add session support (required for browser path)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Add YARP reverse proxy services
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Register custom transform provider
builder.Services.AddSingleton<ResponseRewriteTransformProvider>();

// Register custom HTTP client factory for proxy support
builder.Services.AddSingleton<IForwarderHttpClientFactory, ProxyHttpClientFactory>();

// Register browser pool services
builder.Services.AddSingleton<BrowserPoolManager>();
builder.Services.AddSingleton<BrowserFetchService>();
builder.Services.AddSingleton<IntelligentRequestRouter>();

// Add health checks
builder.Services.AddHealthChecks();

var app = builder.Build();

// Get logger for startup
var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();

// Initialize browser pool if browser path is enabled
var browserPathEnabled = builder.Configuration.GetValue("Scaling:BrowserPath:Enabled", false);
if (browserPathEnabled)
{
    startupLogger.LogInformation("Initializing browser pool...");
    var browserPoolManager = app.Services.GetRequiredService<BrowserPoolManager>();
    await browserPoolManager.InitializeAsync();
    startupLogger.LogInformation("Browser pool initialized successfully");
}

// Configure middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// Enable session (required for browser path)
app.UseSession();

// Map health check endpoint
app.MapHealthChecks("/health");

// Add hybrid routing middleware (decides between fast path and browser path)
app.UseMiddleware<HybridRoutingMiddleware>();

// Map reverse proxy with custom transforms (used for fast path)
app.MapReverseProxy(proxyPipeline =>
{
    // Add custom transform provider
    proxyPipeline.Use((context, next) =>
    {
        // Log incoming requests
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogInformation(
            "Proxying request: {Method} {Path} from {RemoteIp}",
            context.Request.Method,
            context.Request.Path,
            context.Connection.RemoteIpAddress);

        return next();
    });
});

// Log startup information
var proxyConfig = builder.Configuration.GetSection("Proxy");
var destinationConfig = builder.Configuration.GetSection("ReverseProxy:Clusters:default-cluster:Destinations:primary:Address");

startupLogger.LogInformation("==========================================");
startupLogger.LogInformation("YARP Proxy Service Starting...");
startupLogger.LogInformation("==========================================");
startupLogger.LogInformation("Proxy Server: {ProxyUri}", proxyConfig["Uri"]);
startupLogger.LogInformation("Destination: {Destination}", destinationConfig.Value);
startupLogger.LogInformation("URL Rewriting: {Enabled}", builder.Configuration.GetValue<bool>("UrlRewriting:Enabled"));
startupLogger.LogInformation("Browser Path: {Enabled}", browserPathEnabled);

if (browserPathEnabled)
{
    var poolSize = builder.Configuration.GetValue("BrowserPool:MaxSize", 10);
    startupLogger.LogInformation("Browser Pool Max Size: {MaxSize}", poolSize);
}

startupLogger.LogInformation("==========================================");

// Handle graceful shutdown
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    startupLogger.LogInformation("Application is shutting down...");

    if (browserPathEnabled)
    {
        var browserPoolManager = app.Services.GetRequiredService<BrowserPoolManager>();
        browserPoolManager.CloseAllAsync().GetAwaiter().GetResult();
    }
});

app.Run();
