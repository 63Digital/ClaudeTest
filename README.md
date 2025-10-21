# YARP Proxy Service

A reverse proxy service built with YARP (Yet Another Reverse Proxy) that routes traffic through an external proxy provider to access location-blocked websites, with comprehensive URL rewriting capabilities.

## Features

- **Reverse Proxy**: Built on Microsoft's YARP framework for high-performance proxying
- **External Proxy Support**: Route traffic through HTTP/HTTPS proxy servers
- **URL Rewriting**: Automatically rewrites URLs in responses to maintain proxy transparency
- **Content Type Handling**: Smart rewriting for HTML, CSS, JavaScript, and JSON content
- **Redirect Handling**: Properly rewrites Location headers for 301/302/307/308 redirects
- **Configurable**: Easy configuration via `appsettings.json`
- **Authentication**: Supports proxy authentication with username/password
- **Logging**: Comprehensive logging for debugging and monitoring

## Architecture

```
Client Request → YARP Server → External Proxy → Blocked Website
                     ↓
              URL Rewriting
                     ↓
Client Response ← Modified Content
```

## Prerequisites

- .NET 8.0 SDK or later
- An external HTTP/HTTPS proxy server (if accessing blocked content)

## Installation

1. Clone the repository:
```bash
git clone <repository-url>
cd ClaudeTest
```

2. Restore dependencies:
```bash
cd YarpProxyService
dotnet restore
```

3. Configure your settings in `appsettings.json` (see Configuration section below)

4. Run the application:
```bash
dotnet run
```

The service will start on `http://localhost:5000` by default (or the configured port).

## Configuration

### Basic Configuration

Edit `appsettings.json` to configure the proxy service:

```json
{
  "Proxy": {
    "Uri": "http://your-proxy-server:8080",
    "Username": "proxy-username",
    "Password": "proxy-password",
    "Type": "Http",
    "TimeoutSeconds": 30
  },
  "ReverseProxy": {
    "Routes": {
      "default-route": {
        "ClusterId": "default-cluster",
        "Match": {
          "Path": "{**catch-all}"
        }
      }
    },
    "Clusters": {
      "default-cluster": {
        "Destinations": {
          "primary": {
            "Address": "https://blocked-website.com"
          }
        }
      }
    }
  },
  "UrlRewriting": {
    "Enabled": true,
    "RewriteContentTypes": [
      "text/html",
      "text/css",
      "application/javascript",
      "text/javascript",
      "application/json"
    ]
  }
}
```

### Configuration Options

#### Proxy Settings

- **Uri**: The address of your external proxy server (required)
- **Username**: Username for proxy authentication (optional)
- **Password**: Password for proxy authentication (optional)
- **Type**: Proxy type - `Http`, `Https`, or `Socks5` (currently only Http/Https supported)
- **TimeoutSeconds**: Connection timeout in seconds (default: 30)

#### Reverse Proxy Settings

- **Address**: The destination website you want to access through the proxy

#### URL Rewriting Settings

- **Enabled**: Enable/disable URL rewriting (default: true)
- **RewriteContentTypes**: List of content types to rewrite

### Environment Variables

You can override configuration using environment variables:

```bash
export Proxy__Uri="http://proxy-server:8080"
export Proxy__Username="username"
export Proxy__Password="password"
export ReverseProxy__Clusters__default-cluster__Destinations__primary__Address="https://example.com"
```

## Usage

### Basic Usage

1. Start the service:
```bash
dotnet run
```

2. Access the proxied website through your local server:
```
http://localhost:5000/
```

All requests will be routed through your configured proxy to the destination website.

### Multiple Destinations

To proxy multiple websites, you can add additional routes and clusters:

```json
"ReverseProxy": {
  "Routes": {
    "site1-route": {
      "ClusterId": "site1-cluster",
      "Match": {
        "Hosts": ["site1.localhost"]
      }
    },
    "site2-route": {
      "ClusterId": "site2-cluster",
      "Match": {
        "Hosts": ["site2.localhost"]
      }
    }
  },
  "Clusters": {
    "site1-cluster": {
      "Destinations": {
        "primary": {
          "Address": "https://website1.com"
        }
      }
    },
    "site2-cluster": {
      "Destinations": {
        "primary": {
          "Address": "https://website2.com"
        }
      }
    }
  }
}
```

## Project Structure

```
YarpProxyService/
├── Program.cs                              # Main entry point
├── appsettings.json                        # Configuration
├── appsettings.Development.json            # Development configuration
├── YarpProxyService.csproj                 # Project file
├── Proxy/
│   ├── ProxyHttpClientFactory.cs          # HTTP client with proxy support
│   └── Configuration/
│       └── ProxySettings.cs               # Proxy configuration model
└── Transforms/
    ├── ResponseRewriteTransform.cs        # URL rewriting logic
    └── ResponseRewriteTransformProvider.cs # Transform registration
```

## How It Works

### 1. Request Flow

1. Client sends a request to the YARP service (e.g., `http://localhost:5000/page`)
2. YARP receives the request and applies request transforms (sets X-Forwarded headers)
3. The custom `ProxyHttpClientFactory` creates an HTTP client configured to use the external proxy
4. The request is forwarded through the external proxy to the destination website
5. The destination website responds

### 2. Response Flow

1. YARP receives the response from the destination
2. The `ResponseRewriteTransform` intercepts the response
3. If the content type is rewritable (HTML, CSS, JS, etc.):
   - The response body is read into memory
   - URLs pointing to the destination are rewritten to point to the YARP service
   - The modified content is sent to the client
4. Location headers in redirects are also rewritten
5. The client receives the modified response

### 3. URL Rewriting

The service rewrites URLs in the following patterns:

- **Absolute URLs**: `https://destination.com/path` → `http://localhost:5000/path`
- **Protocol-relative URLs**: `//destination.com/path` → `//localhost:5000/path`
- **Location Headers**: Redirects are rewritten to keep the client proxied

## Troubleshooting

### Common Issues

#### 1. Connection to Proxy Failed

**Error**: `Error connecting to proxy server`

**Solution**:
- Verify the proxy URI is correct
- Check that the proxy server is accessible
- Verify authentication credentials if required

#### 2. SSL/TLS Errors

**Error**: `SSL connection could not be established`

**Solution**:
- Ensure the destination URL uses the correct protocol (http vs https)
- Check if the proxy supports HTTPS tunneling

#### 3. URLs Not Being Rewritten

**Problem**: Links still point to the destination website

**Solution**:
- Check that `UrlRewriting.Enabled` is `true`
- Verify the content type is in the `RewriteContentTypes` list
- Check the logs for rewriting errors
- Some JavaScript-generated URLs may not be rewritable

#### 4. Proxy Authentication Failed

**Error**: `407 Proxy Authentication Required`

**Solution**:
- Verify the username and password are correct
- Check that the credentials are properly configured

### Debug Mode

For detailed logging, set the logging level in `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Yarp": "Debug",
      "YarpProxyService": "Debug"
    }
  }
}
```

## Health Checks

The service exposes a health check endpoint:

```
GET /health
```

Returns `200 OK` if the service is running.

## Limitations

- **JavaScript-generated URLs**: Some URLs created dynamically by JavaScript may not be rewritable
- **WebSockets**: WebSocket connections require additional implementation (future enhancement)
- **CORS**: Some websites may have CORS restrictions that affect proxying
- **Binary Content**: Images, PDFs, and other binary content are passed through without modification

## Security Considerations

- Store proxy credentials securely (use User Secrets in development, environment variables in production)
- Never commit credentials to version control
- The service removes `Content-Security-Policy` and `X-Frame-Options` headers to allow proxying
- Consider implementing rate limiting to prevent abuse
- Use HTTPS for the YARP service in production

## Performance

- Handles concurrent requests efficiently
- Response buffering may increase memory usage for large responses
- URL rewriting adds minimal overhead (< 500ms typically)

## Future Enhancements

- [ ] SOCKS5 proxy support
- [ ] Multiple proxy fallback/rotation
- [ ] WebSocket support
- [ ] Response caching
- [ ] Admin API for runtime configuration
- [ ] Request/response logging with sanitization
- [ ] Docker container support

## Contributing

Contributions are welcome! Please feel free to submit issues or pull requests.

## License

[Specify your license here]

## Acknowledgments

- Built with [YARP](https://microsoft.github.io/reverse-proxy/) by Microsoft
- Uses [ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/)
