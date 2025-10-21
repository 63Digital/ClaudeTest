using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace YarpProxyService.Transforms;

/// <summary>
/// Provides response rewrite transforms for YARP routes.
/// </summary>
public class ResponseRewriteTransformProvider : ITransformProvider
{
    private readonly ILogger<ResponseRewriteTransformProvider> _logger;
    private readonly IConfiguration _configuration;

    public ResponseRewriteTransformProvider(
        ILogger<ResponseRewriteTransformProvider> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public void ValidateRoute(TransformRouteValidationContext context)
    {
        // No specific validation needed for routes
    }

    public void ValidateCluster(TransformClusterValidationContext context)
    {
        // Validate that the cluster has at least one destination
        if (context.Cluster.Destinations == null || !context.Cluster.Destinations.Any())
        {
            context.Errors.Add(new ArgumentException(
                $"Cluster '{context.Cluster.ClusterId}' must have at least one destination."));
        }
    }

    public void Apply(TransformBuilderContext context)
    {
        // Get the destination host from the cluster configuration
        var cluster = context.Cluster;
        var destination = cluster?.Destinations?.Values.FirstOrDefault();

        if (destination?.Address == null)
        {
            _logger.LogWarning(
                "Cannot apply response rewrite transform: No destination address found for route {RouteId}",
                context.Route.RouteId);
            return;
        }

        // Extract the host from the destination address
        if (!Uri.TryCreate(destination.Address, UriKind.Absolute, out var destinationUri))
        {
            _logger.LogError(
                "Invalid destination address format: {Address}",
                destination.Address);
            return;
        }

        var destinationHost = destinationUri.Host;

        _logger.LogInformation(
            "Applying response rewrite transform for route {RouteId} with destination host {Host}",
            context.Route.RouteId,
            destinationHost);

        // Add request header transforms
        context.AddRequestTransform(transformContext =>
        {
            var request = transformContext.HttpContext.Request;

            // Set X-Forwarded headers
            transformContext.ProxyRequest.Headers.TryAddWithoutValidation(
                "X-Forwarded-Host", request.Host.ToString());
            transformContext.ProxyRequest.Headers.TryAddWithoutValidation(
                "X-Forwarded-Proto", request.Scheme);
            transformContext.ProxyRequest.Headers.TryAddWithoutValidation(
                "X-Forwarded-For", transformContext.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");

            // Set the Host header to the destination host
            transformContext.ProxyRequest.Headers.Host = destinationHost;

            return default;
        });

        // Add response rewrite transform
        context.AddResponseTransform(async transformContext =>
        {
            var logger = transformContext.HttpContext.RequestServices
                .GetRequiredService<ILogger<ResponseRewriteTransform>>();

            var transform = new ResponseRewriteTransform(
                destinationHost,
                logger,
                _configuration);

            await transform.ApplyAsync(transformContext);
        });

        // Add response header transforms for common headers
        context.AddResponseHeadersTransform(transformContext =>
        {
            // Remove headers that might cause issues
            transformContext.HttpContext.Response.Headers.Remove("Content-Security-Policy");
            transformContext.HttpContext.Response.Headers.Remove("X-Frame-Options");

            return default;
        });
    }
}
