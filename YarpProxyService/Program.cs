using Yarp.ReverseProxy.Forwarder;
using YarpProxyService.Proxy;
using YarpProxyService.Transforms;

var builder = WebApplication.CreateBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Add YARP reverse proxy services
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Register custom transform provider
builder.Services.AddSingleton<ResponseRewriteTransformProvider>();

// Register custom HTTP client factory for proxy support
builder.Services.AddSingleton<IForwarderHttpClientFactory, ProxyHttpClientFactory>();

// Add health checks (optional but recommended)
builder.Services.AddHealthChecks();

var app = builder.Build();

// Configure middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// Map health check endpoint
app.MapHealthChecks("/health");

// Map reverse proxy with custom transforms
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
var logger = app.Services.GetRequiredService<ILogger<Program>>();
var proxyConfig = builder.Configuration.GetSection("Proxy");
var destinationConfig = builder.Configuration.GetSection("ReverseProxy:Clusters:default-cluster:Destinations:primary:Address");

logger.LogInformation("YARP Proxy Service starting...");
logger.LogInformation("Proxy Server: {ProxyUri}", proxyConfig["Uri"]);
logger.LogInformation("Destination: {Destination}", destinationConfig.Value);
logger.LogInformation("URL Rewriting: {Enabled}", builder.Configuration.GetValue<bool>("UrlRewriting:Enabled"));

app.Run();
